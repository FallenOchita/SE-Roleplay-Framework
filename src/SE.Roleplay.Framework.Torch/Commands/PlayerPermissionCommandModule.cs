using System;
using System.Linq;
using System.Threading.Tasks;
using SE.Roleplay.Framework.Core.Abstractions;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace SE.Roleplay.Framework.Torch.Commands
{
    /// <summary>Admin-only commands for managing persistent permissions of online players.</summary>
    [Category("rp admin permissions")]
    public sealed class PlayerPermissionCommandModule : CommandModule
    {
        internal static IFrameworkContext? Framework { get; set; }

        [Permission(MyPromoteLevel.Admin)]
        [Command("list", "Lists the saved permissions for an online player by SteamID64.")]
        public async void List(string steamId64)
        {
            if (!TryParseSteamId(steamId64, out var steamId)) { Respond("Enter a valid 17-digit SteamID64."); return; }
            try
            {
                var permissions = await RequireFramework().Profiles.GetPermissionsAsync(steamId).ConfigureAwait(false);
                Respond(permissions.Count == 0 ? "This player has no saved permissions." :
                    "Saved permissions for " + steamId64 + ":\n" + string.Join("\n", permissions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));
            }
            catch (Exception exception) { Respond("Could not list player permissions: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("grant", "Grants a persistent permission to an online player.")]
        public async void Grant(string steamId64, string permission)
        {
            if (!TryParseSteamId(steamId64, out var steamId)) { Respond("Enter a valid 17-digit SteamID64."); return; }
            if (!IsValidPermission(permission)) { Respond("Enter a permission name such as rp.chat or admin.kick."); return; }
            try
            {
                var framework = RequireFramework();
                await framework.Profiles.GrantPermissionAsync(steamId, permission).ConfigureAwait(false);
                framework.Permissions.Grant(steamId, permission.Trim());
                Respond("Granted '" + permission.Trim() + "' to " + steamId64 + ".");
            }
            catch (Exception exception) { Respond("Could not grant permission: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("revoke", "Revokes a persistent permission from an online player.")]
        public async void Revoke(string steamId64, string permission)
        {
            if (!TryParseSteamId(steamId64, out var steamId)) { Respond("Enter a valid 17-digit SteamID64."); return; }
            if (!IsValidPermission(permission)) { Respond("Enter a permission name such as rp.chat or admin.kick."); return; }
            try
            {
                var framework = RequireFramework();
                await framework.Profiles.RevokePermissionAsync(steamId, permission).ConfigureAwait(false);
                framework.Permissions.Revoke(steamId, permission.Trim());
                Respond("Revoked '" + permission.Trim() + "' from " + steamId64 + ".");
            }
            catch (Exception exception) { Respond("Could not revoke permission: " + exception.Message); }
        }

        [Permission(MyPromoteLevel.Admin)]
        [Command("check", "Checks whether an online player has a permission.")]
        public async void Check(string steamId64, string permission)
        {
            if (!TryParseSteamId(steamId64, out var steamId)) { Respond("Enter a valid 17-digit SteamID64."); return; }
            if (!IsValidPermission(permission)) { Respond("Enter a permission name such as rp.chat or admin.kick."); return; }
            try
            {
                var granted = await RequireFramework().Profiles.HasPermissionAsync(steamId, permission).ConfigureAwait(false);
                Respond(steamId64 + (granted ? " has '" : " does not have '") + permission.Trim() + "'.");
            }
            catch (Exception exception) { Respond("Could not check permission: " + exception.Message); }
        }

        private static bool TryParseSteamId(string value, out ulong steamId)
        {
            steamId = 0;
            return value != null && value.Length == 17 &&
                value.All(character => character >= '0' && character <= '9') &&
                ulong.TryParse(value, out steamId);
        }

        private static bool IsValidPermission(string permission) =>
            !string.IsNullOrWhiteSpace(permission) && permission.Trim().Length <= 100 && !permission.Any(char.IsWhiteSpace);

        private static IFrameworkContext RequireFramework() =>
            Framework ?? throw new InvalidOperationException("The roleplay framework is not initialized.");

        private void Respond(string message) => Context.Torch.Invoke(() => Context.Respond(message));
    }
}