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
    /// <summary>Admin-only controls for persistent public-chat blocked-phrase rules.</summary>
    [Category("rp admin chatmoderation")]
    public sealed class ChatModerationCommandModule : CommandModule
    {
        private static readonly object Sync = new object();
        private static string? _configurationPath;
        private static ChatModerationService? _service;

        internal static ChatModerationService? Service => _service;

        internal static void Configure(string configurationPath, Action<string>? log = null)
        {
            if (string.IsNullOrWhiteSpace(configurationPath))
                throw new ArgumentException("A moderation configuration path is required.", nameof(configurationPath));

            lock (Sync)
            {
                _configurationPath = configurationPath;
                _service = new ChatModerationService();
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

                    var json = File.ReadAllText(configurationPath);
                    var configuration = JsonConvert.DeserializeObject<ChatModerationConfiguration>(json)
                        ?? throw new InvalidDataException("The chat moderation configuration is empty or invalid.");
                    if (configuration.Version != 1)
                        throw new InvalidDataException("Unsupported chat moderation configuration version.");

                    _service.ReplaceRules(configuration.BlockedPhrases ?? Array.Empty<string>(), configuration.Enabled);
                }
                catch (Exception exception)
                {
                    _service.IsEnabled = false;
                    log?.Invoke("Chat moderation configuration could not be loaded; phrase matching has been disabled until an administrator repairs/reloads it. " + exception.Message);
                }
            }
        }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                _service = null;
                _configurationPath = null;
            }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("status", "Shows public-chat moderation status and the number of blocked phrases.")]
        public void Status()
        {
            var service = RequireService();
            Respond("Public chat moderation is " + (service.IsEnabled ? "ENABLED" : "DISABLED") +
                " with " + service.GetBlockedPhrases().Count + " blocked phrase rule(s).");
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("list", "Lists configured blocked phrases.")]
        public void List()
        {
            var phrases = RequireService().GetBlockedPhrases().Take(30).ToArray();
            Respond(phrases.Length == 0
                ? "No blocked phrases are configured."
                : "BLOCKED CHAT PHRASES (first 30)\n" + string.Join("\n", phrases));
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("addword", "Adds a blocked phrase. Quote phrases containing spaces.")]
        public void AddWord(string phrase)
        {
            var service = RequireService();
            lock (Sync)
            {
                try
                {
                    if (!service.AddBlockedPhrase(phrase))
                    {
                        Respond("That blocked phrase already exists.");
                        return;
                    }

                    try { SaveLocked(); }
                    catch
                    {
                        service.RemoveBlockedPhrase(phrase);
                        throw;
                    }

                    Respond("Added blocked phrase: " + phrase.Trim());
                }
                catch (Exception exception)
                {
                    Respond("Could not add blocked phrase: " + exception.Message);
                }
            }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("removeword", "Removes an existing blocked phrase.")]
        public void RemoveWord(string phrase)
        {
            var service = RequireService();
            lock (Sync)
            {
                try
                {
                    if (!service.RemoveBlockedPhrase(phrase))
                    {
                        Respond("That blocked phrase was not found.");
                        return;
                    }

                    try { SaveLocked(); }
                    catch
                    {
                        service.AddBlockedPhrase(phrase);
                        throw;
                    }

                    Respond("Removed blocked phrase: " + phrase.Trim());
                }
                catch (Exception exception)
                {
                    Respond("Could not remove blocked phrase: " + exception.Message);
                }
            }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("enable", "Enables public-chat blocked-phrase moderation.")]
        public void Enable() => SetEnabled(true);

        [Permission(MyPromoteLevel.Admin)]
        [Command("disable", "Disables blocked-phrase matching without deleting rules.")]
        public void Disable() => SetEnabled(false);

        [Permission(MyPromoteLevel.Admin)]
        [Command("reload", "Reloads moderation rules from the server configuration file.")]
        public void Reload()
        {
            try
            {
                var path = _configurationPath;
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException("Chat moderation is not configured.");

                var json = File.ReadAllText(path);
                var configuration = JsonConvert.DeserializeObject<ChatModerationConfiguration>(json)
                    ?? throw new InvalidDataException("The chat moderation configuration is empty or invalid.");
                if (configuration.Version != 1)
                    throw new InvalidDataException("Unsupported chat moderation configuration version.");

                RequireService().ReplaceRules(
                    configuration.BlockedPhrases ?? Array.Empty<string>(),
                    configuration.Enabled);
                Respond("Reloaded " + RequireService().GetBlockedPhrases().Count + " blocked phrase rule(s).");
            }
            catch (Exception exception)
            {
                Respond("Could not reload chat moderation rules: " + exception.Message);
            }
        }

        private void SetEnabled(bool enabled)
        {
            var service = RequireService();
            lock (Sync)
            {
                var previous = service.IsEnabled;
                service.IsEnabled = enabled;
                try
                {
                    SaveLocked();
                    Respond("Public-chat blocked-phrase moderation " + (enabled ? "enabled." : "disabled; rules were retained."));
                }
                catch (Exception exception)
                {
                    service.IsEnabled = previous;
                    Respond("Could not save moderation settings: " + exception.Message);
                }
            }
        }

        private static void SaveLocked()
        {
            var path = _configurationPath;
            var service = _service;
            if (string.IsNullOrWhiteSpace(path) || service == null)
                throw new InvalidOperationException("Chat moderation is not configured.");

            var configuration = new ChatModerationConfiguration
            {
                Version = 1,
                Enabled = service.IsEnabled,
                BlockedPhrases = service.GetBlockedPhrases().ToArray()
            };
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(configuration, Formatting.Indented));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }

        private static ChatModerationService RequireService() =>
            _service ?? throw new InvalidOperationException("Chat moderation is not initialized.");

        private void Respond(string message) =>
            Context.Torch.Invoke(() => Context.Respond(message));

        private sealed class ChatModerationConfiguration
        {
            public int Version { get; set; } = 1;
            public bool Enabled { get; set; } = true;
            public string[]? BlockedPhrases { get; set; } = Array.Empty<string>();
        }
    }
}
