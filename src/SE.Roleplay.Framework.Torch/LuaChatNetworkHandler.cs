using System;
using System.Collections.Generic;
using System.Linq;
using SE.Roleplay.Framework.Core.Abstractions;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace SE.Roleplay.Framework.Torch
{
    internal static class LuaChatNetworkHandler
    {
        private const ushort NetworkChannel = 51348;
        private const double LocalChatRangeMetres = 500d;

        public static void Send(ulong senderSteamId, string channel, string message, IFrameworkLogger? logger)
        {
            try
            {
                if (senderSteamId == 0 || string.IsNullOrWhiteSpace(message) || message.Length > 280 ||
                    (channel != "local" && channel != "faction"))
                    return;

                var multiplayer = MyAPIGateway.Multiplayer;
                var playersApi = MyAPIGateway.Players;
                var session = MyAPIGateway.Session;
                if (multiplayer == null || !multiplayer.IsServer || playersApi == null || session == null)
                    return;

                var connected = new List<IMyPlayer>();
                playersApi.GetPlayers(connected);
                var sender = connected.FirstOrDefault(player => player != null && player.SteamUserId == senderSteamId);
                if (sender == null || sender.IdentityId == 0)
                    return;

                var recipients = new List<IMyPlayer>();
                if (channel == "faction")
                {
                    var senderFaction = session.Factions?.TryGetPlayerFaction(sender.IdentityId);
                    if (senderFaction == null)
                        return;

                    foreach (var player in connected)
                    {
                        if (player == null || player.SteamUserId == 0 || player.IdentityId == 0)
                            continue;
                        var faction = session.Factions?.TryGetPlayerFaction(player.IdentityId);
                        if (faction != null && faction.FactionId == senderFaction.FactionId)
                            recipients.Add(player);
                    }
                }
                else
                {
                    Vector3D senderPosition;
                    try
                    {
                        senderPosition = sender.GetPosition();
                    }
                    catch
                    {
                        return;
                    }

                    var rangeSquared = LocalChatRangeMetres * LocalChatRangeMetres;
                    foreach (var player in connected)
                    {
                        if (player == null || player.SteamUserId == 0)
                            continue;
                        try
                        {
                            var distanceSquared = Vector3D.DistanceSquared(senderPosition, player.GetPosition());
                            if (!double.IsNaN(distanceSquared) && !double.IsInfinity(distanceSquared) &&
                                distanceSquared <= rangeSquared)
                                recipients.Add(player);
                        }
                        catch
                        {
                            // Players without a controlled entity have no usable local-chat position.
                        }
                    }
                }

                var senderName = new string((sender.DisplayName ?? "Player").Where(character => !char.IsControl(character)).Take(64).ToArray());
                if (string.IsNullOrWhiteSpace(senderName))
                    senderName = "Player";
                var packet = new LuaChatNetworkMessage
                {
                    Channel = channel,
                    Sender = senderName,
                    Text = message
                }.Serialize();
                if (packet.Length > 1024)
                    return;

                foreach (var recipient in recipients.GroupBy(player => player.SteamUserId).Select(group => group.First()))
                {
                    if (recipient.SteamUserId != 0)
                        multiplayer.SendMessageTo(NetworkChannel, packet, recipient.SteamUserId, true);
                }
            }
            catch (Exception exception)
            {
                logger?.Warn("RP " + channel + " chat routing failed: " + exception.Message);
            }
        }
    }
}