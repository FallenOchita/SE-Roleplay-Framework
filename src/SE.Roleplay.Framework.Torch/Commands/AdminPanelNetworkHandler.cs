using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace SE.Roleplay.Framework.Torch.Commands
{
    /// <summary>
    /// Server-side secure-message bridge for the in-game admin panel.
    /// Sender identity comes from the secure-message callback, never from packet data.
    /// </summary>
    internal static class AdminPanelNetworkHandler
    {
        private const ushort NetworkChannel = 51347;
        private static Action<ushort, byte[], ulong, bool>? _handler;
        private static readonly ConcurrentDictionary<ulong, long> LastRequestTicks =
            new ConcurrentDictionary<ulong, long>();
        private static int _registered;

        public static void Start()
        {
            var multiplayer = MyAPIGateway.Multiplayer;
            if (multiplayer == null || !multiplayer.IsServer ||
                System.Threading.Interlocked.Exchange(ref _registered, 1) != 0)
                return;

            _handler = OnSecureMessage;
            multiplayer.RegisterSecureMessageHandler(NetworkChannel, _handler);
        }

        public static void Stop()
        {
            if (System.Threading.Interlocked.Exchange(ref _registered, 0) == 0)
                return;

            var multiplayer = MyAPIGateway.Multiplayer;
            if (multiplayer != null && _handler != null)
                multiplayer.UnregisterSecureMessageHandler(NetworkChannel, _handler);

            _handler = null;
            LastRequestTicks.Clear();
        }

        private static bool TryAcquireRequestSlot(ulong steamId)
        {
            var now = DateTime.UtcNow.Ticks;
            var minimumInterval = TimeSpan.FromMilliseconds(400).Ticks;
            while (true)
            {
                if (LastRequestTicks.TryGetValue(steamId, out var previous))
                {
                    if (now - previous < minimumInterval)
                        return false;
                    if (LastRequestTicks.TryUpdate(steamId, now, previous))
                        return true;
                }
                else if (LastRequestTicks.TryAdd(steamId, now))
                {
                    return true;
                }
            }
        }

        private static void OnSecureMessage(ushort channel, byte[] payload, ulong senderSteamId, bool isSenderServer)
        {
            if (channel != NetworkChannel || isSenderServer || senderSteamId == 0 ||
                payload == null || payload.Length == 0 || payload.Length > 3500)
                return;

            if (!TryAcquireRequestSlot(senderSteamId))
                return;

            // The sender SteamID is supplied by SE's secure callback. A SteamID inside
            // the serialized request is ignored and is never used for authorization.
            var session = MyAPIGateway.Session;
            if (session == null || session.GetUserPromoteLevel(senderSteamId) < MyPromoteLevel.Admin)
            {
                Send(senderSteamId, new AdminPanelMessage
                {
                    Success = false,
                    ErrorCode = "forbidden",
                    Message = "Server administrator permission is required."
                });
                return;
            }

            AdminPanelMessage request;
            try
            {
                request = AdminPanelMessage.Deserialize(payload);
                if (request.ProtocolVersion != AdminPanelMessage.CurrentProtocolVersion)
                    throw new InvalidOperationException("Unsupported protocol version.");
            }
            catch
            {
                Send(senderSteamId, new AdminPanelMessage
                {
                    Success = false,
                    ErrorCode = "invalid_request",
                    Message = "The administration request was invalid."
                });
                return;
            }

            _ = Task.Run(async () =>
            {
                AdminPanelMessage response;
                try
                {
                    response = await ServerMembershipCommandModule.ExecutePanelRequestAsync(request)
                        .ConfigureAwait(false);
                }
                catch
                {
                    response = new AdminPanelMessage
                    {
                        RequestId = request.RequestId,
                        Success = false,
                        ErrorCode = "server_error",
                        Message = "The server could not complete that administration request."
                    };
                }

                response.RequestId = request.RequestId;
                response.ProtocolVersion = AdminPanelMessage.CurrentProtocolVersion;
                Send(senderSteamId, response);
            });
        }

        private static void Send(ulong recipient, AdminPanelMessage response)
        {
            try
            {
                var multiplayer = MyAPIGateway.Multiplayer;
                if (multiplayer == null || !multiplayer.IsServer)
                    return;

                var payload = response.Serialize();
                // MyAPIGateway multiplayer calls must be made on the SE1 game thread.
                // The directory request completes on a worker thread, so marshal the send.
                if (payload.Length <= 4000)
                {
                    MyAPIGateway.Utilities?.InvokeOnGameThread(() =>
                    {
                        var gameMultiplayer = MyAPIGateway.Multiplayer;
                        if (gameMultiplayer != null && gameMultiplayer.IsServer)
                            gameMultiplayer.SendMessageTo(NetworkChannel, payload, recipient, true);
                    });
                }
            }
            catch
            {
                // A disconnected recipient or unload must not destabilize the Torch server.
            }
        }
    }
}
