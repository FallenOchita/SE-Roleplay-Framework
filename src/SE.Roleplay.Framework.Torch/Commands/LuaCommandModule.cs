using System;
using System.Linq;
using SE.Roleplay.Framework.Scripting;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace SE.Roleplay.Framework.Torch.Commands
{
    /// <summary>Routes player-entered commands to loaded Lua modules after host-side permission checks.</summary>
    [Category("rp script")]
    public sealed class LuaCommandModule : CommandModule
    {
        internal static LuaScriptHost? Host { get; set; }

        [Permission(MyPromoteLevel.None)]
        [Command("run", "Runs a registered Lua RP command. Example: !rp script run greet Alex")]
        public void Run(string commandName, string arguments = "")
        {
            var platformId = Context.Player?.SteamUserId ?? 0;
            if (platformId == 0)
            {
                Respond("These commands must be used by a player in-game.");
                return;
            }

            var parsedArguments = (arguments ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(32)
                .ToArray();
            var host = Host;
            if (host == null)
            {
                Respond("Lua RP commands are not enabled on this server.");
                return;
            }

            host.DispatchCommand(platformId, Context.Player?.DisplayName ?? "Player",
                commandName, parsedArguments, Respond);
        }

        private void Respond(string message) => Context.Torch.Invoke(() => Context.Respond(message));
    }
}