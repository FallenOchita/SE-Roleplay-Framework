using System;
using Sandbox.ModAPI;
using Sandbox.Graphics.GUI;
using SE.Roleplay.Framework.Client.Networking;
using VRage.Game.Components;
using VRage.Input;

namespace SE.Roleplay.Framework.Client
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class AdminPanelSessionComponent : MySessionComponentBase
    {
        // Reserve and document a framework-specific channel. Keep in sync with the server component.
        internal const ushort NetworkChannel = 51347;
        private bool _handlerRegistered;
        private bool _chatHandlerRegistered;
        private bool _screenOpen;

        public override void LoadData()
        {
            if (MyAPIGateway.Multiplayer == null || MyAPIGateway.Utilities == null)
                return;

            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NetworkChannel, OnSecureMessage);
            _handlerRegistered = true;
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(LuaChatMessage.NetworkChannel, OnLuaChatMessage);
            _chatHandlerRegistered = true;
        }

        public override void UpdateAfterSimulation()
        {
            if (_screenOpen || MyAPIGateway.Input == null || MyAPIGateway.Session == null)
                return;

            if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F6) && IsLocalAdmin())
                OpenPanel();
        }

        private static bool IsLocalAdmin()
        {
            var session = MyAPIGateway.Session;
            var player = session?.LocalHumanPlayer;
            return player != null && session!.IsUserAdmin(player.SteamUserId);
        }

        private void OpenPanel()
        {
            if (_screenOpen)
                return;

            _screenOpen = true;
            var screen = new UI.AdminPanelScreen(
                RequestSnapshot,
                AddMember,
                RemoveMember,
                SetEnabled,
                () => _screenOpen = false);
            MyGuiSandbox.AddScreen(screen);
            RequestSnapshot();
        }

        private static void Send(AdminPanelMessage message)
        {
            var multiplayer = MyAPIGateway.Multiplayer;
            if (multiplayer == null)
                return;

            var payload = AdminPanelMessage.Serialize(message);
            multiplayer.SendMessageToServer(NetworkChannel, payload);
        }

        private static void RequestSnapshot() =>
            Send(new AdminPanelMessage { Operation = "GetSnapshot" });

        private static void AddMember(string steamId64, string note) =>
            Send(new AdminPanelMessage
            {
                Operation = "AddMember",
                SteamId64 = steamId64 ?? string.Empty,
                Note = note ?? string.Empty
            });

        private static void RemoveMember(string steamId64) =>
            Send(new AdminPanelMessage
            {
                Operation = "RemoveMember",
                SteamId64 = steamId64 ?? string.Empty
            });

        private static void SetEnabled(bool enabled) =>
            Send(new AdminPanelMessage { Operation = "SetEnabled", Enabled = enabled });

        private void OnSecureMessage(ushort channel, byte[] data, ulong senderSteamId, bool isSenderServer)
        {
            // A client accepts panel responses only from the connected server.
            if (!isSenderServer || channel != NetworkChannel)
                return;

            try
            {
                var response = AdminPanelMessage.Deserialize(data);
                if (response.ProtocolVersion != AdminPanelMessage.CurrentProtocolVersion)
                    return;

                // Secure-message callbacks are not guaranteed to run on the GUI thread.
                // Marshal all control updates through the SE1 game-thread dispatcher.
                var utilities = MyAPIGateway.Utilities;
                if (utilities != null)
                    utilities.InvokeOnGameThread(() => UI.AdminPanelScreen.ApplyResponse(response));
            }
            catch (Exception)
            {
                // Malformed server payloads are ignored; never let network data break the UI loop.
            }
        }

        private void OnLuaChatMessage(ushort channel, byte[] data, ulong senderSteamId, bool isSenderServer)
        {
            if (channel != LuaChatMessage.NetworkChannel || !isSenderServer || data == null)
                return;

            try
            {
                var message = LuaChatMessage.Deserialize(data);
                if (message.ProtocolVersion != LuaChatMessage.CurrentProtocolVersion ||
                    (message.Channel != "local" && message.Channel != "faction") ||
                    string.IsNullOrWhiteSpace(message.Sender) ||
                    string.IsNullOrWhiteSpace(message.Text) ||
                    message.Sender.Length > 64 || message.Text.Length > 280)
                    return;

                var utilities = MyAPIGateway.Utilities;
                if (utilities == null)
                    return;

                utilities.InvokeOnGameThread(() =>
                    MyAPIGateway.Utilities?.ShowMessage(
                        "[RP:" + message.Channel.ToUpperInvariant() + "] " + message.Sender,
                        message.Text));
            }
            catch (Exception)
            {
                // Malformed or incompatible server chat packets are ignored.
            }
        }

        protected override void UnloadData()
        {
            if (_handlerRegistered && MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NetworkChannel, OnSecureMessage);

            _handlerRegistered = false;
            if (_chatHandlerRegistered && MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(LuaChatMessage.NetworkChannel, OnLuaChatMessage);
            _chatHandlerRegistered = false;
            _screenOpen = false;
            base.UnloadData();
        }
    }
}
