using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace SE.Roleplay.Framework.Torch.Commands
{
    /// <summary>
    /// Server-owner allowlist controls. The same admin commands are available in the Torch
    /// server console and in-game admin chat; the menu command prints the in-game action menu.
    /// </summary>
    [Category("rp admin allowlist")]
    public sealed class ServerMembershipCommandModule : CommandModule
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private static Uri? _directoryBaseUri;
        private static string? _serverId;
        private static string? _apiKey;

        internal static void Configure()
        {
            var url = Environment.GetEnvironmentVariable("SERPF_DIRECTORY_URL");
            _serverId = Environment.GetEnvironmentVariable("SERPF_SERVER_ID");
            _apiKey = Environment.GetEnvironmentVariable("SERPF_SERVER_API_KEY");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                string.IsNullOrWhiteSpace(_serverId) ||
                string.IsNullOrWhiteSpace(_apiKey))
            {
                _directoryBaseUri = null;
                return;
            }

            _directoryBaseUri = new Uri(uri.GetLeftPart(UriPartial.Authority) +
                uri.AbsolutePath.TrimEnd('/') + "/");
        }

        internal static void Shutdown()
        {
            _directoryBaseUri = null;
            _serverId = null;
            _apiKey = null;
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("menu", "Shows the server allowlist administration menu.")]
        public async void Menu()
        {
            try
            {
                var snapshot = await GetSnapshotAsync().ConfigureAwait(false);
                var enabled = (bool?)snapshot["allowlistEnabled"] == true;
                var members = snapshot["members"] as JArray ?? new JArray();
                Respond("SERVER ALLOWLIST ADMIN MENU\nEnforcement: " + (enabled ? "ENABLED" : "DISABLED") +
                    "\nMembers: " + members.Count +
                    "\nCommands: status | list | enable | disable | add <SteamID64> [note] | remove <SteamID64>");
            }
            catch (Exception exception) { Respond("Allowlist menu unavailable: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("status", "Shows allowlist enforcement status.")]
        public async void Status()
        {
            try
            {
                var snapshot = await GetSnapshotAsync().ConfigureAwait(false);
                var enabled = (bool?)snapshot["allowlistEnabled"] == true;
                var members = snapshot["members"] as JArray ?? new JArray();
                Respond("Allowlist enforcement is " + (enabled ? "ENABLED" : "DISABLED") +
                    "; " + members.Count + " member(s) listed.");
            }
            catch (Exception exception) { Respond("Could not read allowlist status: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("list", "Lists all members on this server's allowlist.")]
        public async void List()
        {
            try
            {
                var members = (await GetSnapshotAsync().ConfigureAwait(false))["members"] as JArray ?? new JArray();
                if (members.Count == 0) { Respond("The server allowlist is empty."); return; }
                var lines = members.Select(item =>
                    (string?)item?["steamId64"] ?? (string?)item?["SteamId64"] ?? "Unknown");
                Respond("SERVER ALLOWLIST MEMBERS\n" + string.Join("\n", lines));
            }
            catch (Exception exception) { Respond("Could not list allowlist members: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("enable", "Enables allowlist enforcement on this server.")]
        public async void Enable() => await SetEnabledAsync(true).ConfigureAwait(false);

        [Permission(MyPromoteLevel.Admin)]
        [Command("disable", "Disables allowlist enforcement without deleting members.")]
        public async void Disable() => await SetEnabledAsync(false).ConfigureAwait(false);

        [Permission(MyPromoteLevel.Admin)]
        [Command("add", "Adds a SteamID64 to this server's allowlist. Quote notes containing spaces.")]
        public async void Add(string steamId64, string note = "")
        {
            if (!IsSteamId64(steamId64) || (note?.Length ?? 0) > 200)
            {
                Respond("Use a valid 17-digit SteamID64 and an optional note of 200 characters or fewer.");
                return;
            }
            try
            {
                await SendAsync(HttpMethod.Post, "allowlist/members",
                    new { steamId64, note = note ?? string.Empty }).ConfigureAwait(false);
                Respond("Added " + steamId64 + " to this server's allowlist.");
            }
            catch (Exception exception) { Respond("Could not add allowlist member: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("remove", "Removes a SteamID64 from this server's allowlist.")]
        public async void Remove(string steamId64)
        {
            if (!IsSteamId64(steamId64)) { Respond("Enter a valid 17-digit SteamID64."); return; }
            try
            {
                await SendAsync(HttpMethod.Delete, "allowlist/members/" + Uri.EscapeDataString(steamId64), null)
                    .ConfigureAwait(false);
                Respond("Removed " + steamId64 + " from this server's allowlist.");
            }
            catch (Exception exception) { Respond("Could not remove allowlist member: " + exception.Message); }
        }

        private async Task SetEnabledAsync(bool enabled)
        {
            try
            {
                await SendAsync(HttpMethod.Put, "allowlist", new { allowlistEnabled = enabled }).ConfigureAwait(false);
                Respond("Allowlist enforcement " + (enabled ? "enabled." : "disabled. The member list was retained."));
            }
            catch (Exception exception) { Respond("Could not update allowlist enforcement: " + exception.Message); }
        }

        private async Task<JObject> GetSnapshotAsync()
        {
            var response = await SendAsync(HttpMethod.Get, "allowlist", null).ConfigureAwait(false);
            return response ?? new JObject();
        }

        private async Task<JObject?> SendAsync(HttpMethod method, string relativePath, object? body)
        {
            if (_directoryBaseUri == null || string.IsNullOrWhiteSpace(_serverId) || string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("Configure SERPF_DIRECTORY_URL, SERPF_SERVER_ID, and SERPF_SERVER_API_KEY in the Torch server process.");
            var path = "api/servers/" + Uri.EscapeDataString(_serverId) + "/" + relativePath;
            using (var request = new HttpRequestMessage(method, new Uri(_directoryBaseUri, path)))
            {
                request.Headers.TryAddWithoutValidation("X-Server-Api-Key", _apiKey);
                if (body != null)
                    request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request).ConfigureAwait(false))
                {
                    var payload = response.Content == null ? string.Empty :
                        await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Directory returned HTTP " + (int)response.StatusCode + ".");
                    return string.IsNullOrWhiteSpace(payload) ? null : JObject.Parse(payload);
                }
            }
        }

        private static bool IsSteamId64(string? value) =>
            value != null && value.Length == 17 && value.All(character => character >= '0' && character <= '9');

        private void Respond(string message)
        {
            Context.Torch.Invoke(() => Context.Respond(message));
        }
    }
}
