using System;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using SE.Roleplay.Framework.Client.Networking;
using VRageMath;

namespace SE.Roleplay.Framework.Client.UI
{
    internal sealed class AdminPanelScreen : MyGuiScreenBase
    {
        private static AdminPanelScreen? _active;
        private readonly Action _refresh;
        private readonly Action<string, string> _add;
        private readonly Action<string> _remove;
        private readonly Action<bool> _setEnabled;
        private readonly Action _closed;
        private readonly MyGuiControlTextbox _steamIdInput;
        private readonly MyGuiControlTextbox _noteInput;
        private readonly MyGuiControlLabel _status;
        private readonly MyGuiControlLabel _members;
        private bool _allowlistEnabled;

        public AdminPanelScreen(
            Action refresh,
            Action<string, string> add,
            Action<string> remove,
            Action<bool> setEnabled,
            Action closed)
            : base(new Vector2(0.5f, 0.5f), null, new Vector2(0.72f, 0.72f))
        {
            _refresh = refresh;
            _add = add;
            _remove = remove;
            _setEnabled = setEnabled;
            _closed = closed;
            _steamIdInput = new MyGuiControlTextbox(position: new Vector2(0f, -0.12f), size: new Vector2(0.42f, 0.04f));
            _noteInput = new MyGuiControlTextbox(position: new Vector2(0f, -0.045f), size: new Vector2(0.42f, 0.04f));
            _status = new MyGuiControlLabel(position: new Vector2(0f, -0.29f), text: "Connecting to server...");
            _members = new MyGuiControlLabel(position: new Vector2(0f, 0.13f), text: "No allowlist data received.");
            _active = this;
        }

        public override string GetFriendlyName() => "SE Roleplay Framework - Server Administration";

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            Controls.Add(new MyGuiControlLabel(position: new Vector2(0f, -0.34f), text: "SERVER ALLOWLIST ADMINISTRATION"));
            Controls.Add(_status);
            Controls.Add(_members);
            Controls.Add(new MyGuiControlLabel(position: new Vector2(0f, -0.17f), text: "SteamID64"));
            Controls.Add(_steamIdInput);
            Controls.Add(new MyGuiControlLabel(position: new Vector2(0f, -0.095f), text: "Note (optional)"));
            Controls.Add(_noteInput);

            Controls.Add(new MyGuiControlButton(
                position: new Vector2(-0.19f, 0.0f),
                text: "Add member",
                onButtonClick: _ => AddMember()));
            Controls.Add(new MyGuiControlButton(
                position: new Vector2(0.19f, 0.0f),
                text: "Remove member",
                onButtonClick: _ => RemoveMember()));
            Controls.Add(new MyGuiControlButton(
                position: new Vector2(-0.19f, 0.27f),
                text: "Refresh",
                onButtonClick: _ => _refresh()));
            Controls.Add(new MyGuiControlButton(
                position: new Vector2(0.19f, 0.27f),
                text: "Toggle enforcement",
                onButtonClick: _ => _setEnabled(!_allowlistEnabled)));
            Controls.Add(new MyGuiControlButton(
                position: new Vector2(0f, 0.35f),
                text: "Close",
                onButtonClick: _ => HandleClose()));
        }

        private void AddMember()
        {
            var steamId = _steamIdInput.Text.ToString().Trim();
            if (!IsSteamId64(steamId))
            {
                _status.Text = "Enter a valid 17-digit SteamID64.";
                return;
            }

            var note = _noteInput.Text == null ? string.Empty : _noteInput.Text.ToString();
            if (note.Length > 200)
            {
                _status.Text = "Notes must be 200 characters or fewer.";
                return;
            }

            _add(steamId, note);
            _status.Text = "Add request sent; awaiting server response...";
        }

        private void RemoveMember()
        {
            var steamId = _steamIdInput.Text.ToString().Trim();
            if (!IsSteamId64(steamId))
            {
                _status.Text = "Enter a valid 17-digit SteamID64.";
                return;
            }

            _remove(steamId);
            _status.Text = "Remove request sent; awaiting server response...";
        }

        internal static void ApplyResponse(AdminPanelMessage response)
        {
            var screen = _active;
            if (screen == null)
                return;

            screen._allowlistEnabled = response.AllowlistEnabled;
            screen._status.Text = string.IsNullOrWhiteSpace(response.Message)
                ? (response.Success ? "Server response received." : "Request failed: " + response.ErrorCode)
                : response.Message;

            if (response.Members != null)
            {
                var visibleMembers = response.Members.Take(40).ToArray();
                screen._members.Text = "Enforcement: " + (response.AllowlistEnabled ? "ENABLED" : "DISABLED") +
                    "\nMembers (" + response.MemberCount + " total; showing " + visibleMembers.Length + "):\n" +
                    string.Join("\n", visibleMembers) +
                    (response.Members.Length > visibleMembers.Length ? "\n...more members omitted" : string.Empty);
            }
        }

        private void HandleClose() => CloseScreen();

        public override bool CloseScreen()
        {
            _active = null;
            _closed();
            return base.CloseScreen();
        }

        private static bool IsSteamId64(string value) =>
            value != null && value.Length == 17 && value.All(character => character >= '0' && character <= '9');
    }
}
