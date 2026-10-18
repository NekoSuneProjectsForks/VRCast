using System;
using UnityEngine;
using VRCast.Remote;

namespace VRCast.UI
{
    /// <summary>Session hosting, joining, status and invite controls in the existing Control tab.</summary>
    public sealed class SessionSection
    {
        private readonly SessionClient _client;
        private string _address = "http://127.0.0.1:39580";
        private string _room = string.Empty;
        private string _ticket = string.Empty;
        private string _admin = string.Empty;
        private string _name = "VRCast User";
        private string _displayedRoom = string.Empty;
        private string _pasteStatus = string.Empty;

        public SessionSection(SessionClient client) => _client = client;

        public void Draw()
        {
            if (_client == null) return;
            GuiControls.BeginCard("Shared sessions (experimental)");
            GuiControls.Hint("Host or join a VRCast room. Scene participants are displayed as " +
                "simple capsules while compatible avatar sharing is under development. " +
                "For internet access use HTTPS/WSS through a trusted reverse proxy.");
            GUILayout.Label("Server URL (http(s):// or ws(s)://)");
            _address = GUILayout.TextField(_address, 256);
            GUILayout.Label("Display name");
            _name = GUILayout.TextField(_name, 48);
            GUILayout.Label("Room code");
            _room = GUILayout.TextField(_room, 12);
            GUILayout.Label("Room invite/host ticket");
            _ticket = GUILayout.PasswordField(_ticket, '*', 256);
            if (GUILayout.Button("Paste guest invite / host credentials", GUILayout.ExpandWidth(false)))
                ImportCredentials(GUIUtility.systemCopyBuffer);
            if (!string.IsNullOrEmpty(_pasteStatus)) GuiControls.Hint(_pasteStatus);

            GUILayout.BeginHorizontal();
            GUI.enabled = !_client.IsBusy && !_client.IsConnected;
            if (GUILayout.Button("Join room", GUILayout.ExpandWidth(false)))
                _client.Join(_address, _room, _ticket, _name);
            GUI.enabled = true;
            if (GUILayout.Button("Disconnect", GUILayout.ExpandWidth(false))) _client.Disconnect();
            GUILayout.EndHorizontal();

            GUILayout.Label("Server admin token (only for creating a room)");
            _admin = GUILayout.PasswordField(_admin, '*', 256);
            GUI.enabled = !_client.IsBusy && !_client.IsConnected;
            if (GUILayout.Button("Create room and host", GUILayout.ExpandWidth(false)))
            {
                _client.Host(_address, _admin, _name);
                _admin = string.Empty;
            }
            GUI.enabled = true;
            GuiControls.Hint("Keep the admin/host tokens private. Invite tokens allow guests into " +
                "this room. Room data is temporary and is erased on server restart.");

            GUILayout.Label("Status: " + _client.Status);
            GUILayout.Label("Participants: " + _client.ParticipantCount);
            if (_client.IsConnected)
            {
                if (_displayedRoom != _client.ActiveRoom)
                {
                    _displayedRoom = _client.ActiveRoom;
                    _room = _displayedRoom;
                }
                GUILayout.Label("Current room: " + _client.ActiveRoom);
                GUILayout.Label(_client.IsHost ? "You (host)" : "You (guest)");
                foreach (string participant in _client.RemoteParticipants)
                    GUILayout.Label("  • " + participant);
                if (!string.IsNullOrEmpty(_client.InviteTicket))
                {
                    if (GUILayout.Button("Copy guest invite (room, server, ticket)", GUILayout.ExpandWidth(false)))
                        GUIUtility.systemCopyBuffer = "Server: " + _client.ActiveServer + "\nRoom: " + _client.ActiveRoom +
                            "\nTicket: " + _client.InviteTicket;
                }
                if (_client.IsHost && !string.IsNullOrEmpty(_client.HostTicket))
                {
                    if (GUILayout.Button("Copy host credentials", GUILayout.ExpandWidth(false)))
                        GUIUtility.systemCopyBuffer = "Server: " + _client.ActiveServer + "\nRoom: " + _client.ActiveRoom +
                            "\nHost ticket: " + _client.HostTicket;
                    GUI.enabled = !_client.IsBusy;
                    if (GuiControls.DangerButton("Close room for everyone", GUILayout.ExpandWidth(false)))
                        _client.CloseHostedRoom();
                    GUI.enabled = true;
                }
            }
            GuiControls.EndCard();
        }

        private void ImportCredentials(string clipboard)
        {
            if (string.IsNullOrWhiteSpace(clipboard) || clipboard.Length > 4096)
            {
                _pasteStatus = "Clipboard does not contain a VRCast room invitation.";
                return;
            }

            string server = null, room = null, ticket = null;
            foreach (string line in clipboard.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = line.IndexOf(':');
                if (separator < 0) continue;
                string value = line.Substring(separator + 1).Trim();
                switch (line.Substring(0, separator).Trim().ToLowerInvariant())
                {
                    case "server": server = value; break;
                    case "room": room = value; break;
                    case "ticket":
                    case "host ticket": ticket = value; break;
                }
            }

            bool validServer = Uri.TryCreate(server, UriKind.Absolute, out Uri url)
                && (url.Scheme == "http" || url.Scheme == "https"
                    || url.Scheme == "ws" || url.Scheme == "wss") && url.UserInfo.Length == 0;
            if (!validServer || string.IsNullOrWhiteSpace(room) || room.Length > 12
                || string.IsNullOrWhiteSpace(ticket) || ticket.Length > 256)
            {
                _pasteStatus = "Expected Server, Room and Ticket (or Host ticket) lines.";
                return;
            }

            _address = server;
            _room = room;
            _ticket = ticket;
            _pasteStatus = "Room credentials pasted. Select Join room to connect.";
        }
    }
}
