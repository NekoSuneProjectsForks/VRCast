using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRCast.Remote;

namespace VRCast.Tests
{
    /// <summary>Exercises the real Unity client against the headless .NET session server.</summary>
    public sealed class SessionClientIntegrationTests
    {
        private static readonly MethodInfo TickMethod = typeof(SessionClient).GetMethod(
            "Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Tick(SessionClient first, SessionClient second)
        {
            // EditMode does not run MonoBehaviour.Update on ordinary GameObjects.
            TickMethod.Invoke(first, null);
            TickMethod.Invoke(second, null);
        }

        private static void Tick(SessionClient client) => TickMethod.Invoke(client, null);

        [UnityTest]
        public IEnumerator SilentWebSocketTriggersReconnect()
        {
            using (var server = new SilentWebSocketServer())
            {
                var clientObject = new GameObject("Stalled session client");
                SessionClient client = clientObject.AddComponent<SessionClient>();
                try
                {
                    // Keep the production default at 35 seconds; only shorten this
                    // client's deadline for the loopback failure simulation.
                    typeof(SessionClient).GetField("_receiveIdleTimeout",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(client, TimeSpan.FromMilliseconds(500));
                    client.Join(server.Address, "ABCDEF", "test-ticket", "Tester");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                    while (!client.IsConnected && DateTime.UtcNow < deadline)
                    {
                        Tick(client);
                        yield return null;
                    }
                    Assert.That(client.IsConnected, Is.True, client.Status);
                    Assert.That(server.Admissions, Is.EqualTo(1));

                    bool sawDisconnect = false;
                    deadline = DateTime.UtcNow.AddSeconds(8);
                    while ((server.Admissions < 2 || !client.IsConnected) && DateTime.UtcNow < deadline)
                    {
                        Tick(client);
                        if (!client.IsConnected) sawDisconnect = true;
                        yield return null;
                    }
                    Assert.That(sawDisconnect, Is.True, "Silent socket never expired");
                    Assert.That(server.Admissions, Is.GreaterThanOrEqualTo(2),
                        "Client did not reconnect after the receive deadline");
                    Assert.That(client.IsConnected, Is.True, client.Status);
                    Assert.That(server.Error, Is.Null);
                }
                finally
                {
                    client.Disconnect();
                    UnityEngine.Object.DestroyImmediate(clientObject);
                }
            }
        }

        [UnityTest]
        public IEnumerator IncompleteMessageCannotKeepSocketAlive()
        {
            using (var server = new SilentWebSocketServer(true, true))
            {
                var clientObject = new GameObject("Fragmented session client");
                SessionClient client = clientObject.AddComponent<SessionClient>();
                try
                {
                    typeof(SessionClient).GetField("_receiveIdleTimeout",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(client, TimeSpan.FromMilliseconds(500));
                    client.Join(server.Address, "ABCDEF", "test-ticket", "Tester");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                    while (!client.IsConnected && DateTime.UtcNow < deadline)
                    {
                        Tick(client);
                        yield return null;
                    }
                    Assert.That(client.IsConnected, Is.True, client.Status);

                    deadline = DateTime.UtcNow.AddSeconds(8);
                    while (server.Admissions < 2 && DateTime.UtcNow < deadline)
                    {
                        Tick(client);
                        yield return null;
                    }
                    Assert.That(server.Admissions, Is.GreaterThanOrEqualTo(2),
                        "An unfinished WebSocket text message prevented reconnect");
                    Assert.That(server.Error, Is.Null);
                }
                finally
                {
                    client.Disconnect();
                    UnityEngine.Object.DestroyImmediate(clientObject);
                }
            }
        }

        [UnityTest]
        public IEnumerator IncompleteWebSocketHandshakeTimesOut()
        {
            using (var server = new SilentWebSocketServer(false))
            {
                var clientObject = new GameObject("Stalled handshake client");
                SessionClient client = clientObject.AddComponent<SessionClient>();
                try
                {
                    typeof(SessionClient).GetField("_connectTimeout",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(client, TimeSpan.FromMilliseconds(500));
                    client.Join(server.Address, "ABCDEF", "test-ticket", "Tester");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                    while (client.IsBusy && DateTime.UtcNow < deadline)
                    {
                        Tick(client);
                        yield return null;
                    }
                    Assert.That(server.Admissions, Is.EqualTo(1));
                    Assert.That(client.IsBusy, Is.False, "WebSocket handshake stayed pending");
                    Assert.That(client.IsConnected, Is.False);
                    Assert.That(client.Status, Does.Contain("timed out"));
                    Assert.That(server.Error, Is.Null);
                }
                finally
                {
                    client.Disconnect();
                    UnityEngine.Object.DestroyImmediate(clientObject);
                }
            }
        }

        /// <summary>A loopback peer that admits a socket, then sends no heartbeats or close frame.</summary>
        private sealed class SilentWebSocketServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly System.Collections.Generic.List<TcpClient> _clients =
                new System.Collections.Generic.List<TcpClient>();
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            private readonly bool _answerHandshake;
            private readonly bool _leaveFragmentOpen;
            private volatile bool _disposed;
            private int _admissions;

            public string Address { get; }
            public int Admissions => Volatile.Read(ref _admissions);
            public string Error { get; private set; }

            public SilentWebSocketServer(bool answerHandshake = true, bool leaveFragmentOpen = false)
            {
                _answerHandshake = answerHandshake;
                _leaveFragmentOpen = leaveFragmentOpen;
                _listener.Start();
                Address = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
                Task.Run(AcceptLoop);
            }

            private async Task AcceptLoop()
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        TcpClient peer = await _listener.AcceptTcpClientAsync();
                        lock (_clients) _clients.Add(peer);
                        if (!_answerHandshake)
                        {
                            Interlocked.Increment(ref _admissions);
                            await Task.Delay(Timeout.Infinite, _stop.Token);
                            return;
                        }

                        NetworkStream stream = peer.GetStream();
                        var request = new StringBuilder();
                        byte[] oneByte = new byte[1];
                        while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                        {
                            if (request.Length > 8192 || await stream.ReadAsync(oneByte, 0, 1) != 1)
                                throw new IOException("Missing WebSocket upgrade request");
                            request.Append((char)oneByte[0]);
                        }
                        string key = null;
                        foreach (string line in request.ToString().Split('\n'))
                            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                                key = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                        if (string.IsNullOrEmpty(key)) throw new IOException("Missing WebSocket key");
                        byte[] hash;
                        using (var sha = SHA1.Create())
                            hash = sha.ComputeHash(Encoding.ASCII.GetBytes(key
                                + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
                        string response = "HTTP/1.1 101 Switching Protocols\r\n"
                            + "Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "
                            + Convert.ToBase64String(hash) + "\r\n\r\n";
                        byte[] headers = Encoding.ASCII.GetBytes(response);
                        await stream.WriteAsync(headers, 0, headers.Length);
                        const string welcome = "{\"type\":\"welcome\",\"participantId\":\"silent-user\","
                            + "\"resumeToken\":\"silent-token\",\"host\":false,\"participants\":[]}";
                        byte[] payload = Encoding.UTF8.GetBytes(welcome);
                        if (payload.Length >= 126) throw new IOException("Test welcome frame too long");
                        byte[] frame = new byte[payload.Length + 2];
                        frame[0] = 0x81; // final text frame from server (unmasked)
                        frame[1] = (byte)payload.Length;
                        Buffer.BlockCopy(payload, 0, frame, 2, payload.Length);
                        Interlocked.Increment(ref _admissions);
                        await stream.WriteAsync(frame, 0, frame.Length);
                        if (_leaveFragmentOpen)
                        {
                            // First fragment of a text message, without FIN or
                            // a continuation frame. The receive watchdog must
                            // cover the complete message, not only first bytes.
                            byte[] incomplete = { 0x01, 0x01, (byte)'x' };
                            await stream.WriteAsync(incomplete, 0, incomplete.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!_disposed) Error = ex.ToString();
                }
            }

            public void Dispose()
            {
                _disposed = true;
                _stop.Cancel();
                _listener.Stop();
                lock (_clients)
                    foreach (TcpClient client in _clients) client.Dispose();
                _stop.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator HostJoinReconnectAndRevokeRoom()
        {
            string dll = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../SessionServer/bin/Release/net8.0/VRCast.SessionServer.dll"));
            if (!File.Exists(dll))
                Assert.Ignore("Build SessionServer in Release before running the live client integration test.");

            int port;
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                reservation.Start();
                port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            }
            finally { reservation.Stop(); }
            string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var start = new ProcessStartInfo("dotnet", "\"" + dll + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(dll),
            };
            start.EnvironmentVariables["VRCAST_SESSION_BIND"] = "127.0.0.1";
            start.EnvironmentVariables["VRCAST_SESSION_PORT"] = port.ToString();
            start.EnvironmentVariables["VRCAST_SESSION_ADMIN_TOKEN"] = token;

            using (Process server = Process.Start(start))
            {
                Assert.That(server, Is.Not.Null, "Session server failed to start");
                GameObject hostObject = new GameObject("Session integration host");
                GameObject guestObject = new GameObject("Session integration guest");
                SessionClient host = hostObject.AddComponent<SessionClient>();
                SessionClient guest = guestObject.AddComponent<SessionClient>();
                try
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                    bool ready = false;
                    while (DateTime.UtcNow < deadline && !ready && !server.HasExited)
                    {
                        using (var probe = new TcpClient())
                        {
                            try { probe.Connect(IPAddress.Loopback, port); ready = true; }
                            catch (SocketException) { }
                        }
                        yield return null;
                    }
                    Assert.That(ready, Is.True, "Session server did not begin listening");

                    string url = "http://127.0.0.1:" + port;
                    host.Host(url, token, "Host");
                    deadline = DateTime.UtcNow.AddSeconds(15);
                    while (!host.IsConnected && DateTime.UtcNow < deadline)
                    {
                        Tick(host, guest);
                        yield return null;
                    }
                    Assert.That(host.IsConnected, Is.True, host.Status);
                    Assert.That(host.IsHost, Is.True);
                    Assert.That(host.ActiveRoom, Has.Length.EqualTo(6));
                    Assert.That(host.InviteTicket, Is.Not.Empty);

                    guest.Join(url, host.ActiveRoom, host.InviteTicket, "Guest");
                    deadline = DateTime.UtcNow.AddSeconds(15);
                    while ((host.ParticipantCount != 2 || guest.ParticipantCount != 2)
                        && DateTime.UtcNow < deadline)
                    {
                        Tick(host, guest);
                        yield return null;
                    }
                    Assert.That(host.ParticipantCount, Is.EqualTo(2), host.Status);
                    Assert.That(guest.ParticipantCount, Is.EqualTo(2), guest.Status);
                    Assert.That(string.Join(", ", host.RemoteParticipants), Does.Contain("Guest"));

                    // Publish a pose through the Unity client's own socket, then
                    // verify the guest renders a named, positioned placeholder.
                    typeof(SessionClient).GetMethod("SendLive", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(host, new object[] {
                            "{\"type\":\"state\",\"seq\":1,\"state\":{\"avatar\":\"TestAvatar\","
                            + "\"position\":[2,1,3],\"rotation\":[0,0,0,1]}}"
                        });
                    deadline = DateTime.UtcNow.AddSeconds(8);
                    while (!string.Join(", ", guest.RemoteParticipants).Contains("TestAvatar")
                        && DateTime.UtcNow < deadline)
                    {
                        Tick(host, guest);
                        yield return null;
                    }
                    Assert.That(string.Join(", ", guest.RemoteParticipants), Does.Contain("Host (TestAvatar)"));
                    GameObject remoteHost = GameObject.Find("Session participant: Host");
                    Assert.That(remoteHost, Is.Not.Null, "Remote placeholder did not become visible");
                    Assert.That(remoteHost.transform.position, Is.EqualTo(new Vector3(2, 1, 3)));

                    guest.Disconnect();
                    deadline = DateTime.UtcNow.AddSeconds(8);
                    while (host.ParticipantCount != 1 && DateTime.UtcNow < deadline)
                    {
                        Tick(host, guest);
                        yield return null;
                    }
                    Assert.That(host.ParticipantCount, Is.EqualTo(1));

                    guest.Join(url, host.ActiveRoom, host.InviteTicket, "Guest");
                    deadline = DateTime.UtcNow.AddSeconds(15);
                    while ((host.ParticipantCount != 2 || guest.ParticipantCount != 2)
                        && DateTime.UtcNow < deadline)
                    {
                        Tick(host, guest);
                        yield return null;
                    }
                    Assert.That(host.ParticipantCount, Is.EqualTo(2), host.Status);
                    Assert.That(guest.ParticipantCount, Is.EqualTo(2), guest.Status);
                    Assert.That(string.Join(", ", guest.RemoteParticipants), Does.Contain("Host (TestAvatar)"),
                        "Reconnect did not restore the host's last published pose");
                    Assert.That(GameObject.Find("Session participant: Host"), Is.Not.Null);

                    // Hosts who save their host ticket must be able to rejoin from
                    // the ordinary Join UI and still manage/revoke the room.
                    string room = host.ActiveRoom;
                    string hostTicket = host.HostTicket;
                    host.Disconnect();
                    deadline = DateTime.UtcNow.AddSeconds(8);
                    while (guest.ParticipantCount != 1 && DateTime.UtcNow < deadline)
                    {
                        Tick(host, guest);
                        yield return null;
                    }
                    Assert.That(guest.ParticipantCount, Is.EqualTo(1),
                        "Server did not observe the host disconnect");
                    host.Join(url, room, hostTicket, "Host");
                    deadline = DateTime.UtcNow.AddSeconds(15);
                    while ((!host.IsConnected || guest.ParticipantCount != 2) && DateTime.UtcNow < deadline)
                    {
                        Tick(host, guest);
                        yield return null;
                    }
                    Assert.That(host.IsConnected, Is.True, host.Status);
                    Assert.That(host.IsHost, Is.True);
                    Assert.That(host.HostTicket, Is.EqualTo(hostTicket));

                    host.CloseHostedRoom();
                    deadline = DateTime.UtcNow.AddSeconds(15);
                    while ((!host.Status.StartsWith("Room closed", StringComparison.Ordinal)
                            || !guest.Status.StartsWith("Room closed", StringComparison.Ordinal))
                        && DateTime.UtcNow < deadline)
                    {
                        Tick(host, guest);
                        yield return null;
                    }
                    Assert.That(host.Status, Is.EqualTo("Room closed"));
                    Assert.That(guest.Status, Is.EqualTo("Room closed by host"));
                    Assert.That(host.IsConnected, Is.False);
                    Assert.That(guest.IsConnected, Is.False);
                }
                finally
                {
                    host.Disconnect();
                    guest.Disconnect();
                    UnityEngine.Object.DestroyImmediate(hostObject);
                    UnityEngine.Object.DestroyImmediate(guestObject);
                    if (!server.HasExited) server.Kill();
                }
            }
        }
    }
}
