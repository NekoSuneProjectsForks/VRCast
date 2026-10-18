using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VRCast.SessionServer;

SessionConfig config = SessionConfig.FromEnvironment();
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{config.BindAddress}:{config.Port}");
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<SessionHub>();
builder.Services.AddHostedService<RoomCleanupService>();

WebApplication app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", protocolVersion = SessionConfig.ProtocolVersion }));
app.MapGet("/api/status", (SessionHub hub) => Results.Ok(hub.Status));
app.MapPost("/api/rooms", (HttpContext context, SessionHub hub) =>
{
    if (!SessionConfig.SecretMatches(Bearer(context), config.AdminToken)) return Results.Unauthorized();
    object? room = hub.CreateRoom();
    return room == null ? Results.StatusCode(503) : Results.Ok(room);
});
app.MapDelete("/api/rooms/{code}", (string code, HttpContext context, SessionHub hub) =>
    hub.DeleteRoom(code, Bearer(context)) ? Results.NoContent() : Results.NotFound());
app.Map("/ws", async (HttpContext context, SessionHub hub, ILoggerFactory loggerFactory) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }
    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
    ILogger logger = loggerFactory.CreateLogger("SessionSocket");
    SessionRoom? room = null;
    SessionMember? member = null;
    using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    try
    {
        using CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        handshake.CancelAfter(TimeSpan.FromSeconds(5));
        JsonElement? initial = await ReadFrame(socket, handshake.Token);
        if (initial == null || !SessionProtocol.TryJoin(initial.Value, out JoinRequest join))
        {
            await SendError(socket, "invalid_join_or_version", lifetime.Token);
            return;
        }
        if (!hub.TryJoin(join, socket, out room, out member, out string reason))
        {
            await SendError(socket, reason, lifetime.Token);
            return;
        }

        // Only the writer loop sends after admission. WebSocket permits one sender and one receiver.
        SessionMember client = member!;
        Task sender = SendLoop(socket, client, lifetime.Token);
        Task ping = PingLoop(client, lifetime.Token);
        try
        {
            while (socket.State == WebSocketState.Open && !lifetime.IsCancellationRequested)
            {
                JsonElement? message = await ReadFrame(socket, lifetime.Token);
                if (message == null) break;
                string? error = hub.Receive(room!, client, message.Value);
                if (error != null) client.Send(new { type = "error", code = error });
                if (error is "rate_limited" or "disconnected") break;
            }
        }
        finally
        {
            hub.Leave(room!, client);
            lifetime.Cancel();
            try { await Task.WhenAll(sender, ping); } catch (OperationCanceledException) { }
        }
    }
    catch (OperationCanceledException) { }
    catch (WebSocketException exception)
    {
        logger.LogDebug("WebSocket connection closed: {Code}", exception.WebSocketErrorCode);
    }
    catch (JsonException)
    {
        logger.LogDebug("Received invalid JSON frame");
    }
    finally
    {
        if (room != null && member != null) hub.Leave(room, member);
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", CancellationToken.None); }
            catch (WebSocketException) { socket.Abort(); }
        }
    }
});

app.Run();

static string? Bearer(HttpContext context)
{
    string header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
}

static async Task<JsonElement?> ReadFrame(WebSocket socket, CancellationToken cancellation)
{
    byte[] bytes = new byte[SessionProtocol.MaxFrameBytes];
    int offset = 0;
    WebSocketReceiveResult result;
    do
    {
        if (offset == bytes.Length) throw new JsonException("Frame too large");
        result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes, offset, bytes.Length - offset), cancellation);
        if (result.MessageType == WebSocketMessageType.Close) return null;
        if (result.MessageType != WebSocketMessageType.Text) throw new JsonException("Text frame required");
        offset += result.Count;
    } while (!result.EndOfMessage);
    using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(0, offset),
        new JsonDocumentOptions { MaxDepth = 12, CommentHandling = JsonCommentHandling.Disallow });
    return document.RootElement.Clone();
}

static async Task SendError(WebSocket socket, string code, CancellationToken cancellation)
{
    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { type = "error", code });
    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation);
}

static async Task SendLoop(WebSocket socket, SessionMember member, CancellationToken cancellation)
{
    await foreach (string message in member.Outbox.Reader.ReadAllAsync(cancellation))
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation);
    }
    if (member.RoomClosed && socket.State == WebSocketState.Open)
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Room closed", cancellation);
}

static async Task PingLoop(SessionMember member, CancellationToken cancellation)
{
    using PeriodicTimer timer = new(TimeSpan.FromSeconds(10));
    while (await timer.WaitForNextTickAsync(cancellation)) member.Send(new { type = "ping" });
}

public sealed class RoomCleanupService(SessionHub hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken)) hub.Cleanup();
    }
}

public partial class Program { }
