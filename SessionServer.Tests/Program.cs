using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VRCast.SessionServer;

static void Assert(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine($"PASS {description}");
}

static JsonElement Parse(string value) => JsonDocument.Parse(value).RootElement.Clone();

Assert(SessionProtocol.TryState(Parse("{\"type\":\"state\",\"seq\":1,\"state\":{\"position\":[1,2,3],\"rotation\":[0,0,0,1],\"expressions\":{\"Smile\":0.5}}}"), out _, out _), "valid actor state accepted");
Assert(!SessionProtocol.TryState(Parse("{\"type\":\"state\",\"seq\":1,\"state\":{\"position\":[1,2,1e309]}}"), out _, out _), "nonfinite position blocked");
Assert(!SessionProtocol.TryState(Parse("{\"type\":\"state\",\"seq\":1,\"state\":{\"command\":\"shutdown\"}}"), out _, out _), "unknown commands blocked");
Assert(!SessionProtocol.TryState(Parse("{\"type\":\"state\",\"seq\":1,\"state\":{\"expressions\":{\"Smile\":2}}}"), out _, out _), "out-of-range expression blocked");
Assert(!SessionProtocol.TryJoin(Parse("{\"type\":\"join\",\"version\":999,\"room\":\"ABCDEF\",\"ticket\":\"AAAAAAAAAAAAAAAAAAAAAAAA\",\"name\":\"Guest\"}"), out _), "incompatible protocol rejected");
Assert(!SessionConfig.SecretMatches("wrong", "secret"), "bad secrets rejected");

using TcpListener reserve = new(IPAddress.Loopback, 0);
reserve.Start();
int port = ((IPEndPoint)reserve.LocalEndpoint).Port;
reserve.Stop();
string token = SessionConfig.NewSecret();
ProcessStartInfo info = new("dotnet", $"\"{typeof(SessionConfig).Assembly.Location}\"")
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
};
info.Environment["VRCAST_SESSION_BIND"] = "127.0.0.1";
info.Environment["VRCAST_SESSION_PORT"] = port.ToString();
info.Environment["VRCAST_SESSION_ADMIN_TOKEN"] = token;
using Process process = Process.Start(info) ?? throw new Exception("Server start failed");
try
{
    using HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    bool ready = false;
    for (int attempt = 0; attempt < 80; attempt++)
    {
        try { ready = (await http.GetAsync("/healthz")).IsSuccessStatusCode; }
        catch (HttpRequestException) { }
        if (ready) break;
        await Task.Delay(100);
    }
    Assert(ready, "server starts and reports healthy");
    Assert((await http.PostAsync("/api/rooms", null)).StatusCode == HttpStatusCode.Unauthorized,
        "room creation requires admin token");

    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using HttpResponseMessage created = await http.PostAsync("/api/rooms", null);
    created.EnsureSuccessStatusCode();
    JsonElement room = Parse(await created.Content.ReadAsStringAsync());
    string code = room.GetProperty("code").GetString()!;
    string hostTicket = room.GetProperty("hostTicket").GetString()!;
    string inviteTicket = room.GetProperty("inviteTicket").GetString()!;
    Assert(code.Length == 6 && hostTicket != inviteTicket, "unique room and separate role tickets");

    Uri address = new($"ws://127.0.0.1:{port}/ws");
    static async Task Send(ClientWebSocket socket, object value)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None);
    }
    static async Task<JsonElement> Read(ClientWebSocket socket)
    {
        byte[] bytes = new byte[32768];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(4));
        WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), timeout.Token);
        if (result.MessageType == WebSocketMessageType.Close) throw new Exception("Unexpected WebSocket close");
        return Parse(Encoding.UTF8.GetString(bytes, 0, result.Count));
    }
    static async Task<JsonElement> ReadType(ClientWebSocket socket, string wanted)
    {
        for (int i = 0; i < 5; i++)
        {
            JsonElement message = await Read(socket);
            string? type = message.GetProperty("type").GetString();
            if (type == "ping") await Send(socket, new { type = "pong" });
            if (type == wanted) return message;
        }
        throw new Exception($"Never received message type {wanted}");
    }
    using ClientWebSocket bad = new();
    await bad.ConnectAsync(address, CancellationToken.None);
    await Send(bad, new { type = "join", version = 1, room = code, ticket = "invalid-invalid-invalid-invalid", name = "Bad" });
    Assert((await Read(bad)).GetProperty("code").GetString() == "invalid_room_or_ticket",
        "invalid room credentials rejected");

    using ClientWebSocket host = new();
    await host.ConnectAsync(address, CancellationToken.None);
    await Send(host, new { type = "join", version = 1, room = code, ticket = hostTicket, name = "Host" });
    JsonElement hostWelcome = await Read(host);
    string hostId = hostWelcome.GetProperty("participantId").GetString()!;
    Assert(hostWelcome.GetProperty("host").GetBoolean(), "host admitted with role");

    using ClientWebSocket guest = new();
    await guest.ConnectAsync(address, CancellationToken.None);
    await Send(guest, new { type = "join", version = 1, room = code, ticket = inviteTicket, name = "Guest" });
    JsonElement guestWelcome = await Read(guest);
    string resumeId = guestWelcome.GetProperty("participantId").GetString()!;
    string resumeToken = guestWelcome.GetProperty("resumeToken").GetString()!;
    Assert(guestWelcome.GetProperty("participants")[0].GetProperty("participantId").GetString() == hostId,
        "new arrivals receive participant snapshot");
    Assert((await Read(host)).GetProperty("type").GetString() == "participant_joined", "join event delivered");

    await Send(host, new { type = "state", seq = 1, state = new { avatar = "Alice", position = new[] { 1, 2, 3 }, rotation = new[] { 0, 0, 0, 1 }, expressions = new { Smile = 0.9 }, motion = "idle" } });
    JsonElement received = await Read(guest);
    Assert(received.GetProperty("type").GetString() == "state" && received.GetProperty("state").GetProperty("avatar").GetString() == "Alice",
        "avatar state relayed to remote participant");

    await Send(guest, new { type = "scene", seq = 1, name = "Reset" });
    Assert((await Read(guest)).GetProperty("code").GetString() == "host_only", "guest cannot issue room-wide scene events");
    await Send(host, new { type = "scene", seq = 2, name = "CameraCut" });
    Assert((await Read(guest)).GetProperty("name").GetString() == "CameraCut", "host scene events relayed");

    await Send(host, new { type = "state", seq = 1, state = new { avatar = "stale" } });
    Assert((await Read(host)).GetProperty("code").GetString() == "stale_sequence", "old updates rejected");
    await Send(host, new { type = "state", seq = 3, state = new { position = new[] { 999999, 0, 0 } } });
    Assert((await Read(host)).GetProperty("code").GetString() == "invalid_message", "invalid coordinates rejected");

    await guest.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    Assert((await Read(host)).GetProperty("type").GetString() == "participant_left", "disconnect event delivered");
    using ClientWebSocket restored = new();
    await restored.ConnectAsync(address, CancellationToken.None);
    await Send(restored, new { type = "join", version = 1, room = code, ticket = inviteTicket, name = "Guest", resumeId, resumeToken });
    JsonElement restoredWelcome = await Read(restored);
    Assert(restoredWelcome.GetProperty("participantId").GetString() == resumeId, "participant resumes within grace window");
    Assert(restoredWelcome.GetProperty("participants")[0].GetProperty("state").GetProperty("avatar").GetString() == "Alice",
        "reconnect receives last known actor snapshot");
    Assert((await ReadType(host, "participant_joined")).GetProperty("type").GetString() == "participant_joined", "reconnect broadcast delivered");

    using HttpResponseMessage deleted = await http.DeleteAsync($"/api/rooms/{code}");
    Assert(deleted.StatusCode == HttpStatusCode.NoContent, "room can be revoked with admin credential");
    Assert((await ReadType(host, "room_closed")).GetProperty("type").GetString() == "room_closed",
        "host receives room closure before WebSocket closes");
    Assert((await ReadType(restored, "room_closed")).GetProperty("type").GetString() == "room_closed",
        "guest receives room closure before WebSocket closes");
    Assert((await http.GetStringAsync("/api/status")).Contains("\"rooms\":0"), "revoked room removed from status");
}
finally
{
    if (!process.HasExited) process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();
}

Console.WriteLine("All session-server checks passed.");
