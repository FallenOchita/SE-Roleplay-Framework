using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SE.Roleplay.Framework.Core;
using SE.Roleplay.Framework.Core.Abstractions;
using SE.Roleplay.Framework.Core.Events;
using SE.Roleplay.Framework.Core.Identity;
using SE.Roleplay.Framework.Core.Permissions;
using SE.Roleplay.Framework.Core.Persistence;
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

            _sessionManager = torch.Managers.GetManager<ITorchSessionManager>();
            if (_sessionManager == null)
                throw new InvalidOperationException("Torch did not provide ITorchSessionManager.");

            _sessionManager.SessionStateChanged += OnSessionStateChanged;
            _nextAutosaveUtc = DateTime.UtcNow.AddSeconds(options.AutosaveIntervalSeconds);

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

            _ = SaveAllProfilesAsync("scheduled autosave");
        }

        private void OnSessionStateChanged(ITorchSession session, TorchSessionState state)
        {
            if (state == TorchSessionState.Loaded)
                AttachSession(session);
            else if (state == TorchSessionState.Unloading || state == TorchSessionState.Unloaded)
            {
                DetachSession();
                _ = SaveAllProfilesAsync("world session shutdown");
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

            _multiplayer.PlayerJoined += OnPlayerJoined;
            _multiplayer.PlayerLeft += OnPlayerLeft;
            _context?.Logger.Info("Attached Torch player lifecycle events.");
        }

        private void DetachSession()
        {
            if (_multiplayer == null)
                return;

            _multiplayer.PlayerJoined -= OnPlayerJoined;
            _multiplayer.PlayerLeft -= OnPlayerLeft;
            _multiplayer = null;
        }

        private void OnPlayerJoined(IPlayer player)
        {
            if (player == null || player.SteamId == 0 || _context == null)
                return;

            var operation = ConnectPlayerAsync(player.SteamId, player.Name);
            _playerOperations[player.SteamId] = operation;
        }

        private void OnPlayerLeft(IPlayer player)
        {
            if (player == null || player.SteamId == 0 || _context == null)
                return;

            _playerOperations[player.SteamId] = DisconnectPlayerAsync(player.SteamId);
        }

        private async Task ConnectPlayerAsync(ulong steamId, string playerName)
        {
            try
            {
                await _context!.Sessions.ConnectAsync(steamId, playerName).ConfigureAwait(false);
                _context.Logger.Info("Loaded profile for " + playerName + " (" + steamId + ").");
            }
            catch (Exception exception)
            {
                _context!.Logger.Error("Could not load profile for Steam account " + steamId + ".", exception);
            }
        }

        private async Task DisconnectPlayerAsync(ulong steamId)
        {
            try
            {
                if (_playerOperations.TryGetValue(steamId, out var pendingConnect))
                    await pendingConnect.ConfigureAwait(false);

                await _context!.Sessions.DisconnectAsync(steamId).ConfigureAwait(false);
                _context.Logger.Info("Saved and unloaded profile for Steam account " + steamId + ".");
            }
            catch (Exception exception)
            {
                _context!.Logger.Error("Could not save profile for Steam account " + steamId + ".", exception);
            }
            finally
            {
                _playerOperations.TryRemove(steamId, out _);
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

            _context?.Logger.Info("Torch adapter stopped.");
            base.Dispose();
        }
    }
}
