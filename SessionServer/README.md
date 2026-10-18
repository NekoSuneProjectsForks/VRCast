# VRCast Session Server (protocol v1)

Standalone, headless room service for synchronising small VRCast sessions over WebSockets. No Unity, database, GPU, avatar files, or audio/video streaming are required on the server.

## Run locally

Requires .NET 8 SDK/runtime. Configure a **random** admin secret before starting:

```powershell
$env:VRCAST_SESSION_ADMIN_TOKEN = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet run --project SessionServer/VRCast.SessionServer.csproj
```

Default binding is `127.0.0.1:39580`. To allow LAN connections, set `VRCAST_SESSION_BIND` to your machine's LAN IP (or `0.0.0.0`) and configure firewall access. The admin secret is required even for localhost. Do not expose a plain HTTP/WS endpoint on the public internet; terminate HTTPS/WSS at a reverse proxy (e.g. Caddy) and configure the proxy to pass WebSocket upgrades. Control rooms and tickets are held in memory and expire when the server restarts.

For Docker, copy `.env.example` to `.env`, replace the admin secret, then run from `SessionServer/`:

```sh
docker compose up -d --build
```

The compose file binds localhost by default; set `VRCAST_SESSION_PUBLISH_ADDRESS=0.0.0.0` for LAN. Configuration is environment-based. No mutable state volume is needed for ephemeral rooms. `GET /healthz` is the health check, and `GET /api/status` returns aggregate counts without secrets.

## Host and join

Create a room (example uses `curl`; pass the admin secret only to the server you operate):

```sh
curl -X POST http://127.0.0.1:39580/api/rooms -H 'Authorization: Bearer YOUR_ADMIN_TOKEN'
```

Response:

```json
{"code":"ABCDEF","inviteTicket":"...","hostTicket":"...","protocolVersion":1}
```

Give guests the **room code and invite ticket**, and give the host the **host ticket**. Never share the admin token or host ticket with guests. To close a room, send `DELETE /api/rooms/ABCDEF` with a bearer `hostTicket` or admin token. `POST /api/rooms` is intentionally restricted by the server-wide admin token.

### VRCast desktop Control tab

In VRCast, open **Control > Shared sessions (experimental)**. Enter the server URL (such as `http://127.0.0.1:39580` on the same computer), and choose a display name. The **Create room and host** button requires the server administrator's token; it creates a room and connects the host automatically. Use **Copy guest invite** to share the server URL, room code, and guest ticket. Guests can click **Paste guest invite / host credentials** to fill all three fields from that text, then select **Join room**. The Control tab lists connected participants and the avatar names they publish. Remote participants are represented by simple capsules in the Unity scene; their own VRM files, face tracking, and body motion are not yet streamed or reproduced.

The host can use **Close room for everyone** to revoke the room and notify its members before disconnecting them, or **Copy host credentials** to save the host-only ticket. A host rejoining with these saved credentials through **Join room** regains the ability to close the room, although the guest invite ticket must still be kept separately. **Disconnect** leaves the room; it does not revoke it. After an unexpected network interruption, the app automatically retries for up to 25 seconds, preserving the host's invitation controls and attempting to resume the same identity within the server's 30-second grace period. **Disconnect** cancels retries. If the server assigns a new identity, the client resets its state sequence. Tickets and server administrator credentials are deliberately not saved in VRCast settings. Only share invitations over a private channel. Do not expose a non-TLS remote server to the internet.

The connection URL is `ws://127.0.0.1:39580/ws` (use `wss://` for TLS). After the WebSocket connects, its **first message** must be a JSON join payload:

```json
{"type":"join","version":1,"room":"ABCDEF","ticket":"ROOM_TICKET","name":"Streamer"}
```

The server returns `welcome` containing `participantId`, `resumeToken`, `host`, and the current participants and their last states. Other clients receive `participant_joined`. After disconnect, other clients receive `participant_left`; a participant may reconnect within 30 seconds with `resumeId` and `resumeToken` in their join payload. The role must match. Resume tokens are rotated after reconnect. Two clients using the same invite ticket receive distinct participant identities.

## Messages

Only these messages are accepted after joining:

```json
{"type":"state","seq":1,"state":{"avatar":"MyAvatar","position":[0,0,0],"rotation":[0,0,0,1],"expressions":{"Smile":0.4},"motion":"idle"}}
{"type":"scene","seq":2,"name":"CameraCut"}
{"type":"pong"}
```

Each sender owns its own actor state. The server validates and broadcasts state to the other members as `{type:"state",participantId,seq,state}`; it does not load avatars or execute commands. `scene` events are restricted to the host and are relayed as intent names only. Event handlers should explicitly allowlist events; arbitrary scripts are not executed. Both `state` and `scene` updates need a strictly increasing, **shared** per-participant `seq` counter. Incoming scene parameters, unsupported state keys, or invalid numeric values are rejected with `{type:"error",code:"..."}`. A server `ping` is answered by `pong` and idle clients are disconnected after 45 seconds. The server never transmits microphone recordings, cameras, VRChat credentials, or rendered frames.

Limits: maximum 16 KiB input message, 60 updates/sec/client by default (heartbeat counts), 64 expression values, 64-byte expression key, 64 queued outgoing messages per client (oldest messages dropped under backpressure), 16 clients/room, 100 rooms. Room codes and tickets are cryptographically generated; only hashes are compared for credential checks. Defaults are configurable through environment variables. The server automatically clears empty rooms after 10 minutes and stale reconnect state after 30 seconds. Do not place tickets in URLs, logs, telemetry, or screenshots.

## Test, build, release

```sh
dotnet build SessionServer/VRCast.SessionServer.csproj -c Release
dotnet run --project SessionServer.Tests/VRCast.SessionServer.Tests.csproj -c Release
dotnet publish SessionServer/VRCast.SessionServer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

CI runs headless protocol and two-client integration tests on Linux; release events package Windows x64, Linux x64, Linux ARM64, and GHCR container tags. The server speaks protocol version **1**. Version mismatches fail the join handshake. Client visual playback, avatar retargeting, live VRChat/Unity tests, and network impairment benchmarking remain separate verification tasks.

### Unity client integration test

Build the session server in Release first. From the repository root, run the Unity 2022.3.62f3 EditMode test fixture using your installed Unity Editor path:

```powershell
& "<path-to-Unity-2022.3.62f3-Editor>/Unity.exe" -batchmode -nographics `
  -projectPath "$PWD/VRCast" -runTests -testPlatform EditMode `
  -testFilter VRCast.Tests.SessionClientIntegrationTests `
  -testResults "$PWD/VRCast/Logs/session-client-tests.xml" `
  -logFile "$PWD/VRCast/Logs/session-client-tests.log"
```

This fixture launches its own loopback .NET server on a free port, hosts and joins using two real `SessionClient` components, verifies the remote placeholder and last pose snapshot, reconnects the guest, revokes the room, and shuts down its test server. When the Release server binary is unavailable, the fixture is skipped. The test runs in Unity EditMode; a separate interactive check with two desktop app instances is still needed for end-to-end GUI verification.
