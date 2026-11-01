using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SE.Roleplay.Framework.Core;
using SE.Roleplay.Framework.Core.Abstractions;
using SE.Roleplay.Framework.Core.Events;
using SE.Roleplay.Framework.Core.Identity;
using SE.Roleplay.Framework.Core.Permissions;
using SE.Roleplay.Framework.Core.Persistence;
using SE.Roleplay.Framework.Torch.Commands;
using Newtonsoft.Json;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Plugins;
using Torch.API.Session;

namespace SE.Roleplay.Framework.Torch
{
    /// <summary>
    /// Torch host adapter. Game-specific types stay in this assembly; the framework core remains host-neutral.
    /// </summary>
    public sealed class TorchRoleplayPlugin : TorchPluginBase
    {
        private readonly ConcurrentDictionary<ulong, Task> _playerOperations = new ConcurrentDictionary<ulong, Task>();
        private IFrameworkContext? _context;
        private ITorchSessionManager? _sessionManager;
        private IMultiplayerManagerBase? _multiplayer;
        private DateTime _nextAutosaveUtc;
        private int _autosaveRunning;
        private bool _disposed;
        private readonly ConcurrentDictionary<ulong, byte> _onlinePlayers = new ConcurrentDictionary<ulong, byte>();
        private CancellationTokenSource? _directoryHeartbeatCancellation;
        private Task? _directoryHeartbeatTask;
        private HttpClient? _directoryHttpClient;
        private Uri? _directoryHeartbeatUri;
        private string? _directoryApiKey;
        private int _directoryMaxPlayers;
        private string _directoryServerVersion = "SE1";

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            IsReloadable = false;

            var server = torch as ITorchServer;
            if (server == null)
                throw new InvalidOperationException("SE Roleplay Framework requires a Torch dedicated server host.");

            var options = new FrameworkOptions
            {
                ServerName = "Space Engineers Roleplay",
                DataDirectory = Path.Combine(server.InstancePath, "Storage", "SE-Roleplay-Framework"),
                AutosaveIntervalSeconds = 300,
                MaximumCharactersPerPlayer = 5
            };

            var identities = new PlayerIdentityService();
            var store = new JsonPlayerDataStore(Path.Combine(options.DataDirectory, "Players"));
            var profiles = new PlayerProfileService(store, identities, options.MaximumCharactersPerPlayer);
            var events = new EventBus();
            var permissions = new PermissionService();
            var sessions = new PlayerSessionService(identities, profiles, events);

            _context = new FrameworkContext(
                events,
                identities,
                sessions,
                profiles,
                permissions,
                store,
                options,
                new TorchFrameworkLogger());
            CharacterCommandModule.Framework = _context;

            _sessionManager = torch.Managers.GetManager<ITorchSessionManager>();
            if (_sessionManager == null)
                throw new InvalidOperationException("Torch did not provide ITorchSessionManager.");

            _sessionManager.SessionStateChanged += OnSessionStateChanged;
            _nextAutosaveUtc = DateTime.UtcNow.AddSeconds(options.AutosaveIntervalSeconds);
            ConfigureDirectoryHeartbeat();

            if (_sessionManager.CurrentSession?.State == TorchSessionState.Loaded)
                AttachSession(_sessionManager.CurrentSession);

            _context.Logger.Info("Torch adapter initialized. Player profiles will be stored in " + options.DataDirectory);
        }

        public override void Update()
        {
            if (_disposed || _context == null || DateTime.UtcNow < _nextAutosaveUtc)
                return;

            _nextAutosaveUtc = DateTime.UtcNow.AddSeconds(_context.Options.AutosaveIntervalSeconds);
            if (Interlocked.Exchange(ref _autosaveRunning, 1) != 0)
                return;

            // JSON serialization and file I/O must not run on Torch's game-update thread.
            _ = Task.Run(() => SaveAllProfilesAsync("scheduled autosave"));
        }

        private void OnSessionStateChanged(ITorchSession session, TorchSessionState state)
        {
            if (state == TorchSessionState.Loaded)
                AttachSession(session);
            else if (state == TorchSessionState.Unloading || state == TorchSessionState.Unloaded)
            {
                DetachSession();
                _ = Task.Run(() => SaveAllProfilesAsync("world session shutdown"));
            }
        }

        private void AttachSession(ITorchSession session)
        {
            if (_multiplayer != null)
                DetachSession();

            _multiplayer = session.Managers.GetManager<IMultiplayerManagerBase>();
            if (_multiplayer == null)
            {
                _context?.Logger.Warn("Torch multiplayer manager was not available; player lifecycle hooks were not attached.");
                return;
            }

            _onlinePlayers.Clear();
            _multiplayer.PlayerJoined += OnPlayerJoined;
            _multiplayer.PlayerLeft += OnPlayerLeft;
            StartDirectoryHeartbeat();
            _context?.Logger.Info("Attached Torch player lifecycle events.");
        }

        private void DetachSession()
        {
            if (_multiplayer == null)
                return;

            _multiplayer.PlayerJoined -= OnPlayerJoined;
            _multiplayer.PlayerLeft -= OnPlayerLeft;
            _multiplayer = null;
            _onlinePlayers.Clear();
        }

        private void OnPlayerJoined(IPlayer player)
        {
            if (player == null || player.SteamId == 0 || _context == null)
                return;

            // Serialize reconnects behind any previous join/leave operation for this Steam account.
            _onlinePlayers.TryAdd(player.SteamId, 0);
            _playerOperations.TryGetValue(player.SteamId, out var previousOperation);
            _playerOperations[player.SteamId] = Task.Run(() => ConnectPlayerAsync(player.SteamId, player.Name, previousOperation));
        }

        private void OnPlayerLeft(IPlayer player)
        {
            if (player == null || player.SteamId == 0 || _context == null)
                return;

            _onlinePlayers.TryRemove(player.SteamId, out _);
            _playerOperations.TryGetValue(player.SteamId, out var pendingConnect);
            var disconnect = Task.Run(() => DisconnectPlayerAsync(player.SteamId, pendingConnect));
            _playerOperations[player.SteamId] = disconnect;

            _ = disconnect.ContinueWith(
                completed =>
                {
                    if (_playerOperations.TryGetValue(player.SteamId, out var current) &&
                        ReferenceEquals(current, disconnect))
                        _playerOperations.TryRemove(player.SteamId, out _);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }


        private void ConfigureDirectoryHeartbeat()
        {
            var directoryUrl = Environment.GetEnvironmentVariable("SERPF_DIRECTORY_URL");
            var serverId = Environment.GetEnvironmentVariable("SERPF_SERVER_ID");
            var apiKey = Environment.GetEnvironmentVariable("SERPF_SERVER_API_KEY");

            if (string.IsNullOrWhiteSpace(directoryUrl) ||
                string.IsNullOrWhiteSpace(serverId) ||
                string.IsNullOrWhiteSpace(apiKey))
            {
                _context?.Logger.Info("Directory heartbeat is disabled. Set SERPF_DIRECTORY_URL, SERPF_SERVER_ID, and SERPF_SERVER_API_KEY to enable it.");
                return;
            }

            if (!Uri.TryCreate(directoryUrl.TrimEnd('/') + "/api/servers/" +
                Uri.EscapeDataString(serverId) + "/heartbeat", UriKind.Absolute, out var heartbeatUri) ||
                heartbeatUri.Scheme != Uri.UriSchemeHttps)
            {
                _context?.Logger.Warn("Directory heartbeat is disabled because SERPF_DIRECTORY_URL must be a valid HTTPS URL.");
                return;
            }

            var maxPlayersText = Environment.GetEnvironmentVariable("SERPF_SERVER_MAX_PLAYERS");
            if (!int.TryParse(maxPlayersText, out _directoryMaxPlayers) ||
                _directoryMaxPlayers < 1 || _directoryMaxPlayers > 10000)
                _directoryMaxPlayers = 32;

            var version = Environment.GetEnvironmentVariable("SERPF_SERVER_VERSION");
            if (!string.IsNullOrWhiteSpace(version))
                _directoryServerVersion = version.Length <= 80 ? version : version.Substring(0, 80);

            _directoryHeartbeatUri = heartbeatUri;
            _directoryApiKey = apiKey;
            _directoryHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _context?.Logger.Info("Directory heartbeat configured for server " + serverId + ".");
        }

        private void StartDirectoryHeartbeat()
        {
            if (_directoryHeartbeatUri == null || _directoryApiKey == null ||
                _directoryHttpClient == null || _directoryHeartbeatTask != null)
                return;

            _directoryHeartbeatCancellation = new CancellationTokenSource();
            var token = _directoryHeartbeatCancellation.Token;
            _directoryHeartbeatTask = Task.Run(() => RunDirectoryHeartbeatAsync(token));
        }

        private async Task RunDirectoryHeartbeatAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var payload = new
                    {
                        players = _onlinePlayers.Count,
                        maxPlayers = _directoryMaxPlayers,
                        version = _directoryServerVersion
                    };
                    var json = JsonConvert.SerializeObject(payload);
                    using (var request = new HttpRequestMessage(HttpMethod.Put, _directoryHeartbeatUri))
                    {
                        request.Headers.TryAddWithoutValidation("X-Server-Api-Key", _directoryApiKey);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        using (var response = await _directoryHttpClient!.SendAsync(
                            request, cancellationToken).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode)
                                _context?.Logger.Warn("Directory heartbeat returned HTTP " +
                                    (int)response.StatusCode + ".");
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _context?.Logger.Warn("Directory heartbeat failed: " + exception.Message);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private void StopDirectoryHeartbeat()
        {
            var cancellation = _directoryHeartbeatCancellation;
            if (cancellation == null)
                return;

            cancellation.Cancel();
            _directoryHeartbeatCancellation = null;
            _directoryHeartbeatTask = null;
            cancellation.Dispose();
        }

        private async Task ConnectPlayerAsync(ulong steamId, string playerName, Task? previousOperation)
        {
            try
            {
                if (previousOperation != null)
                    await previousOperation.ConfigureAwait(false);

                await _context!.Sessions.ConnectAsync(steamId, playerName).ConfigureAwait(false);
                _context.Logger.Info("Loaded profile for " + playerName + " (" + steamId + ").");
            }
            catch (Exception exception)
            {
                _context!.Logger.Error("Could not load profile for Steam account " + steamId + ".", exception);
            }
        }

        private async Task DisconnectPlayerAsync(ulong steamId, Task? pendingConnect)
        {
            try
            {
                if (pendingConnect != null)
                    await pendingConnect.ConfigureAwait(false);

                await _context!.Sessions.DisconnectAsync(steamId).ConfigureAwait(false);
                _context.Logger.Info("Saved and unloaded profile for Steam account " + steamId + ".");
            }
            catch (Exception exception)
            {
                _context!.Logger.Error("Could not save profile for Steam account " + steamId + ".", exception);
            }
        }

        private async Task SaveAllProfilesAsync(string reason)
        {
            try
            {
                if (_context != null)
                {
                    await _context.Profiles.SaveAllAsync().ConfigureAwait(false);
                    _context.Logger.Info("Saved all online profiles (" + reason + ").");
                }
            }
            catch (Exception exception)
            {
                _context?.Logger.Error("Profile save failed (" + reason + ").", exception);
            }
            finally
            {
                Interlocked.Exchange(ref _autosaveRunning, 0);
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_sessionManager != null)
                _sessionManager.SessionStateChanged -= OnSessionStateChanged;

            DetachSession();
            StopDirectoryHeartbeat();

            // Torch's plugin disposal is synchronous. Finish outstanding profile I/O before releasing services.
            try
            {
                Task.WhenAll(_playerOperations.Values.ToArray()).Wait(TimeSpan.FromSeconds(15));
                _context?.Profiles.SaveAllAsync().Wait(TimeSpan.FromSeconds(15));
            }
            catch (Exception exception)
            {
                _context?.Logger.Error("An error occurred while flushing player data during plugin shutdown.", exception);
            }

            try
            {
                _directoryHeartbeatTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException exception)
            {
                _context?.Logger.Warn("Directory heartbeat did not stop cleanly: " + exception.GetBaseException().Message);
            }
            _directoryHttpClient?.Dispose();
            _directoryHttpClient = null;

            CharacterCommandModule.Framework = null;
            _context?.Logger.Info("Torch adapter stopped.");
            base.Dispose();
        }
    }
}
