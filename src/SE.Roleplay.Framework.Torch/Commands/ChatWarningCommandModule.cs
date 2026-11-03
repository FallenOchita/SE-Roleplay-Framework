using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SE.Roleplay.Framework.Core.Chat;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace SE.Roleplay.Framework.Torch.Commands
{
    /// <summary>Admin-only warning issuance and review backed by the server warning ledger.</summary>
    [Category("rp admin chatmoderation")]
    public sealed class ChatWarningCommandModule : CommandModule
    {
        private static readonly object Sync = new object();
        private static string? _configurationPath;
        private static ChatWarningService? _service;
        private static bool _loadFailed;

        internal static void Configure(string configurationPath, Action<string>? log = null)
        {
            lock (Sync)
            {
                _configurationPath = configurationPath;
                _service = new ChatWarningService();
                _loadFailed = false;
                var directory = Path.GetDirectoryName(configurationPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                try
                {
                    if (!File.Exists(configurationPath))
                    {
                        SaveLocked();
                        return;
                    }

                    var configuration = JsonConvert.DeserializeObject<WarningLedgerConfiguration>(
                        File.ReadAllText(configurationPath))
                        ?? throw new InvalidDataException("The warning ledger is empty or invalid.");
                    if (configuration.Version != 1)
                        throw new InvalidDataException("Unsupported warning ledger version.");
                    _service.Replace(configuration.Warnings ?? Array.Empty<ChatWarningRecord>());
                }
                catch (Exception exception)
                {
                    _service = new ChatWarningService();
                    _loadFailed = true;
                    log?.Invoke("Chat warning ledger could not be loaded; warning commands are disabled until the file is repaired. Existing file was not overwritten. " + exception.Message);
                }
            }
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                _service = null;
                _configurationPath = null;
                _loadFailed = false;
            }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("warn", "Issues a persistent chat warning to a player by SteamID64. Quote reasons containing spaces.")]
        public void Warn(string steamId64, string reason)
        {
            if (!TryParseSteamId(steamId64, out var playerId))
            {
                Respond("Enter a valid 17-digit SteamID64 for the player.");
                return;
            }

            var moderatorId = Context.Player?.SteamUserId ?? 0;
            if (moderatorId == 0)
            {
                Respond("Warnings must be issued by an in-game moderator.");
                return;
            }

            lock (Sync)
            {
                var service = RequireService();
                var before = service.Export();
                try
                {
                    var warning = service.AddWarning(playerId, moderatorId, reason);
                    try { SaveLocked(); }
                    catch
                    {
                        service.Replace(before);
                        throw;
                    }

                    Respond("Warning #" + warning.Id.Substring(0, 8) + " issued to " + steamId64 + ".");
                }
                catch (Exception exception)
                {
                    Respond("Could not issue warning: " + exception.Message);
                }
            }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("warnings", "Reviews the latest warnings for a player by SteamID64.")]
        public void Warnings(string steamId64)
        {
            if (!TryParseSteamId(steamId64, out var playerId))
            {
                Respond("Enter a valid 17-digit SteamID64 for the player.");
                return;
            }

            var warnings = RequireService().GetWarnings(playerId).Take(10).ToArray();
            if (warnings.Length == 0)
            {
                Respond("No chat warnings are recorded for " + steamId64 + ".");
                return;
            }

            var lines = warnings.Select((warning, index) =>
                (index + 1) + ". [" + warning.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'") +
                "] Moderator " + warning.ModeratorSteamId + ": " + warning.Reason);
            Respond("CHAT WARNINGS FOR " + steamId64 + " (latest 10)\n" + string.Join("\n", lines));
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("clearwarnings", "Permanently clears a player's warning history. Requires the word CONFIRM.")]
        public void ClearWarnings(string steamId64, string confirmation)
        {
            if (!TryParseSteamId(steamId64, out var playerId))
            {
                Respond("Enter a valid 17-digit SteamID64 for the player.");
                return;
            }
            if (!string.Equals(confirmation, "CONFIRM", StringComparison.Ordinal))
            {
                Respond("This permanently deletes warning history. Repeat with CONFIRM as the second argument.");
                return;
            }

            lock (Sync)
            {
                var service = RequireService();
                var before = service.Export();
                try
                {
                    var removed = service.ClearWarnings(playerId);
                    SaveLocked();
                    Respond("Cleared " + removed + " warning record(s) for " + steamId64 + ".");
                }
                catch (Exception exception)
                {
                    service.Replace(before);
                    Respond("Could not clear warning history: " + exception.Message);
                }
            }
        }

        private static bool TryParseSteamId(string value, out ulong steamId)
        {
            steamId = 0;
            return value != null && value.Length == 17 &&
                value.All(character => character >= '0' && character <= '9') &&
                ulong.TryParse(value, out steamId) && steamId != 0;
        }

        private static ChatWarningService RequireService()
        {
            if (_loadFailed)
                throw new InvalidOperationException("The warning ledger failed to load. Repair ChatWarnings.json before using warning commands.");
            return _service ?? throw new InvalidOperationException("The chat warning ledger is not initialized.");
        }

        private static void SaveLocked()
        {
            var path = _configurationPath;
            var service = _service;
            if (string.IsNullOrWhiteSpace(path) || service == null)
                throw new InvalidOperationException("The chat warning ledger is not initialized.");

            var configuration = new WarningLedgerConfiguration
            {
                Version = 1,
                Warnings = service.Export().ToArray()
            };
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(configuration, Formatting.Indented));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }

        private void Respond(string message) =>
            Context.Torch.Invoke(() => Context.Respond(message));

        private sealed class WarningLedgerConfiguration
        {
            public int Version { get; set; } = 1;
            public ChatWarningRecord[]? Warnings { get; set; } = Array.Empty<ChatWarningRecord>();
        }
    }
}
