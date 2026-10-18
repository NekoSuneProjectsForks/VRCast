using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRCast.Avatars;

namespace VRCast.Remote
{
    /// <summary>
    /// Optional VRCast session client. Networking stays off the Unity render thread; only
    /// Update() reads/writes scene objects. Remote actors are intentionally safe placeholders
    /// until an approved avatar-content sharing protocol exists.
    /// </summary>
    public sealed class SessionClient : MonoBehaviour
    {
        private const float SendInterval = 1f / 20f;
        private const int MaxIncomingPerFrame = 32;
        private const int MaxIncomingQueue = 128;
        // The server sends application-level pings every ten seconds. Detect a
        // stalled connection even when the OS never reports a socket close.
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ReceiveIdleTimeout = TimeSpan.FromSeconds(35);
        private TimeSpan _connectTimeout = ConnectTimeout;
        private TimeSpan _receiveIdleTimeout = ReceiveIdleTimeout;
        private readonly ConcurrentQueue<NetworkEvent> _incoming = new ConcurrentQueue<NetworkEvent>();
        private readonly Dictionary<string, RemoteActor> _actors = new Dictionary<string, RemoteActor>();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private AvatarSession _avatars;
        private ClientWebSocket _socket;
        private CancellationTokenSource _cancellation;
        private volatile bool _welcomed;
        private float _nextSend;
        private long _sequence;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private string _previousAvatar;
        private string _lastRoom;
        private string _lastTicket;
        private string _resumeId;
        private string _resumeToken;
        private string _hostTicket;
        private Uri _serverEndpoint;
        private Uri _lastEndpoint;
        private string _lastDisplayName;
        private string _expectedResumeId;
        private DateTime _disconnectedAt;
        private DateTime _retryUntilUtc;
        private DateTime _retryAtUtc;
        private int _retryAttempt;
        private int _generation;
        private int _pendingSends;
        private CancellationTokenSource _roomCreation;
        private bool _connecting;
        private bool _closingRoom;

        public string Status { get; private set; } = "Disconnected";
        public string ActiveRoom { get; private set; } = string.Empty;
        public string InviteTicket { get; private set; } = string.Empty;
        public int ParticipantCount => _actors.Count + (_welcomed ? 1 : 0);
        public bool IsConnected => _welcomed;
        public bool IsHost { get; private set; }
        public bool IsBusy => _connecting || _roomCreation != null || _closingRoom;
        public string HostTicket => _hostTicket ?? string.Empty;
        public string ActiveServer => _serverEndpoint == null ? string.Empty : new UriBuilder(_serverEndpoint)
        {
            Scheme = _serverEndpoint.Scheme == "wss" ? "https" : "http",
            Path = "/", Query = string.Empty, Fragment = string.Empty,
        }.Uri.ToString();
        public IEnumerable<string> RemoteParticipants
        {
            get
            {
                foreach (RemoteActor actor in _actors.Values) yield return actor.Description;
            }
        }

        public void Initialize(AvatarSession avatars) => _avatars = avatars;

        public void Join(string address, string room, string ticket, string displayName)
        {
            if (_roomCreation != null || _closingRoom) return;
            if (_welcomed)
            {
                Status = "Disconnect from the current room before joining another";
                return;
            }
            if (!TryWebSocketAddress(address, out Uri endpoint))
            {
                Status = "Invalid URL (use ws:// or wss://, or an http(s) server URL)";
                return;
            }
            if (string.IsNullOrWhiteSpace(room) || string.IsNullOrWhiteSpace(ticket))
            {
                Status = "Room code and ticket are required";
                return;
            }
            room = room.Trim().ToUpperInvariant();
            ticket = ticket.Trim();
            bool resume = room == _lastRoom && ticket == _lastTicket
                && endpoint.Equals(_lastEndpoint) && !string.IsNullOrEmpty(_resumeId)
                && !string.IsNullOrEmpty(_resumeToken)
                && (DateTime.UtcNow - _disconnectedAt).TotalSeconds < 30;
            string priorId = resume ? _resumeId : null;
            string priorToken = resume ? _resumeToken : null;
            Disconnect();
            _lastRoom = room;
            _lastTicket = ticket;
            _lastEndpoint = endpoint;
            _lastDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? "VRCast User" : displayName.Trim().Substring(0, Math.Min(48, displayName.Trim().Length));
            _expectedResumeId = priorId;
            _previousAvatar = null; // publish current avatar immediately in the new connection
            if (!resume) _sequence = 0;
            ActiveRoom = room;
            int generation = _generation;
            _cancellation = new CancellationTokenSource();
            _socket = new ClientWebSocket();
            _serverEndpoint = endpoint;
            Status = "Connecting...";
            _connecting = true;
            var join = new JoinMessage
            {
                type = "join", version = 1, room = room, ticket = ticket,
                name = _lastDisplayName,
                resumeId = priorId, resumeToken = priorToken,
            };
            RunConnection(_socket, endpoint, JsonUtility.ToJson(join), _cancellation, generation);
        }

        public void Host(string serverAddress, string adminToken, string displayName)
        {
            if (_roomCreation != null || _closingRoom) return;
            if (!TryWebSocketAddress(serverAddress, out Uri endpoint))
            {
                Status = "Invalid server address";
                return;
            }
            if (string.IsNullOrWhiteSpace(adminToken))
            {
                Status = "Enter server admin token to create a room";
                return;
            }
            Disconnect();
            _serverEndpoint = endpoint;
            _roomCreation = new CancellationTokenSource();
            int generation = _generation;
            Status = "Creating room...";
            // No Unity API is called inside this network task; all UI updates are queued.
            CreateRoom(endpoint, adminToken.Trim(), displayName, _roomCreation, generation);
        }

        private async void CreateRoom(Uri endpoint, string adminToken, string displayName,
            CancellationTokenSource cancellation, int generation)
        {
            try
            {
                var uri = new UriBuilder(endpoint)
                {
                    Scheme = endpoint.Scheme == "wss" ? "https" : "http",
                    Path = "/api/rooms", Query = string.Empty,
                }.Uri;
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                using (var request = new HttpRequestMessage(HttpMethod.Post, uri))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                    using (HttpResponseMessage response = await client.SendAsync(request, cancellation.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            EnqueueStatus("Room creation failed: HTTP " + (int)response.StatusCode, generation);
                            return;
                        }
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        // JsonUtility must run on Unity main thread.
                        _mainThreadActions.Enqueue(() =>
                        {
                            if (generation != _generation) return;
                            RoomReply reply;
                            try { reply = JsonUtility.FromJson<RoomReply>(json); }
                            catch (ArgumentException) { reply = null; }
                            if (reply == null || string.IsNullOrEmpty(reply.code)
                                || string.IsNullOrEmpty(reply.hostTicket) || string.IsNullOrEmpty(reply.inviteTicket))
                            {
                                Status = "Invalid room response";
                                return;
                            }
                            // The HTTP operation has finished. Release the in-flight guard
                            // before Join checks it, while the task owns CTS disposal.
                            if (ReferenceEquals(_roomCreation, cancellation)) _roomCreation = null;
                            Join(endpoint.ToString(), reply.code, reply.hostTicket, displayName);
                            _hostTicket = reply.hostTicket;
                            InviteTicket = reply.inviteTicket;
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is UriFormatException)
            {
                if (!cancellation.IsCancellationRequested)
                    EnqueueStatus("Could not create room: " + ex.GetType().Name, generation);
            }
            finally
            {
                _mainThreadActions.Enqueue(() =>
                {
                    if (ReferenceEquals(_roomCreation, cancellation)) _roomCreation = null;
                    cancellation.Dispose();
                });
            }
        }

        /// <summary>Revoke this hosted room and disconnect every guest.</summary>
        public void CloseHostedRoom()
        {
            if (_closingRoom || !_welcomed || !IsHost || string.IsNullOrEmpty(_hostTicket) || _serverEndpoint == null)
                return;
            _closingRoom = true;
            Status = "Closing room...";
            CloseRoom(_serverEndpoint, ActiveRoom, _hostTicket, _generation);
        }

        private async void CloseRoom(Uri endpoint, string room, string hostTicket, int generation)
        {
            try
            {
                var uri = new UriBuilder(endpoint)
                {
                    Scheme = endpoint.Scheme == "wss" ? "https" : "http",
                    Path = "/api/rooms/" + room, Query = string.Empty, Fragment = string.Empty,
                }.Uri;
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                using (var request = new HttpRequestMessage(HttpMethod.Delete, uri))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostTicket);
                    using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            EnqueueStatus("Could not close room: HTTP " + (int)response.StatusCode, generation);
                            return;
                        }
                        _mainThreadActions.Enqueue(() =>
                        {
                            if (generation != _generation) return;
                            Disconnect();
                            _lastRoom = null;
                            _lastTicket = null;
                            Status = "Room closed";
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is UriFormatException)
            {
                EnqueueStatus("Could not close room: " + ex.GetType().Name, generation);
            }
            finally
            {
                _mainThreadActions.Enqueue(() => { if (generation == _generation) _closingRoom = false; });
            }
        }

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private void EnqueueStatus(string status, int generation) => _mainThreadActions.Enqueue(() =>
        {
            if (generation == _generation) Status = status;
        });

        private async void RunConnection(ClientWebSocket socket, Uri endpoint, string joinPayload,
            CancellationTokenSource source, int generation)
        {
            CancellationToken cancellation = source.Token;
            bool handshakeCompleted = false;
            try
            {
                using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    connecting.CancelAfter(_connectTimeout);
                    // On some Unity/Mono WebSocket implementations ConnectAsync
                    // does not promptly honor token cancellation while the HTTP
                    // upgrade response is missing. Abort the socket on deadline.
                    Task handshake = ConnectAndJoin(socket, endpoint, joinPayload, connecting.Token);
                    if (await Task.WhenAny(handshake, Task.Delay(_connectTimeout, cancellation))
                        .ConfigureAwait(false) != handshake)
                    {
                        connecting.Cancel();
                        socket.Abort();
                        // An aborted handshake may fault after the watchdog wins.
                        _ = handshake.ContinueWith(completed => { _ = completed.Exception; },
                            TaskContinuationOptions.OnlyOnFaulted);
                        cancellation.ThrowIfCancellationRequested();
                        throw new TimeoutException("WebSocket upgrade timed out");
                    }
                    await handshake.ConfigureAwait(false);
                }
                handshakeCompleted = true;
                EnqueueStatus("Connected; waiting for room admission...", generation);
                byte[] buffer = new byte[16384];
                while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
                {
                    // Reset only after a complete message, so a partial frame
                    // cannot keep the connection alive indefinitely.
                    using (var receiving = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        receiving.CancelAfter(_receiveIdleTimeout);
                        int length = 0;
                        WebSocketReceiveResult result;
                        do
                        {
                            if (length == buffer.Length) throw new IOException("Session message too large");
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), receiving.Token)
                                .ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close) return;
                            if (result.MessageType != WebSocketMessageType.Text) throw new IOException("Invalid frame type");
                            length += result.Count;
                        } while (!result.EndOfMessage);
                        if (generation != Volatile.Read(ref _generation)) return;
                        if (_incoming.Count >= MaxIncomingQueue) _incoming.TryDequeue(out _);
                        _incoming.Enqueue(new NetworkEvent(generation, Encoding.UTF8.GetString(buffer, 0, length)));
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                if (generation == Volatile.Read(ref _generation))
                    EnqueueStatus(handshakeCompleted ? "Session receive timed out" : "Session connection timed out", generation);
            }
            catch (TimeoutException)
            {
                if (!cancellation.IsCancellationRequested && generation == Volatile.Read(ref _generation))
                    EnqueueStatus("Session connection timed out", generation);
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException
                || ex is IOException || ex is ObjectDisposedException || ex is HttpRequestException
                || ex is InvalidOperationException)
            {
                if (!cancellation.IsCancellationRequested && generation == Volatile.Read(ref _generation))
                    EnqueueStatus("Session connection failed: " + ex.GetType().Name, generation);
            }
            finally
            {
                if (generation == Volatile.Read(ref _generation))
                    _incoming.Enqueue(new NetworkEvent(generation, "{\"type\":\"closed\"}"));
                socket.Dispose();
                source.Dispose();
            }
        }

        private async Task ConnectAndJoin(ClientWebSocket socket, Uri endpoint, string joinPayload,
            CancellationToken cancellation)
        {
            await socket.ConnectAsync(endpoint, cancellation).ConfigureAwait(false);
            await Send(socket, joinPayload, cancellation).ConfigureAwait(false);
        }

        private async Task Send(ClientWebSocket socket, string json, CancellationToken cancellation)
        {
            await _sendGate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (socket.State != WebSocketState.Open) return;
                byte[] data = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, cancellation)
                    .ConfigureAwait(false);
            }
            finally { _sendGate.Release(); }
        }

        private async void SendLive(string json)
        {
            if (Interlocked.Increment(ref _pendingSends) > 4)
            {
                Interlocked.Decrement(ref _pendingSends);
                return;
            }
            ClientWebSocket socket = _socket;
            CancellationTokenSource token = _cancellation;
            try
            {
                if (socket == null || token == null || token.IsCancellationRequested) return;
                await Send(socket, json, token.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException || ex is ObjectDisposedException
                || ex is OperationCanceledException || ex is InvalidOperationException) { }
            finally { Interlocked.Decrement(ref _pendingSends); }
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out Action action)) action();
            for (int i = 0; i < MaxIncomingPerFrame && _incoming.TryDequeue(out NetworkEvent incoming); i++)
            {
                if (incoming.Generation != _generation) continue;
                Message message;
                try { message = JsonUtility.FromJson<Message>(incoming.Payload); }
                catch (ArgumentException) { continue; }
                if (message == null) continue;
                switch (message.type)
                {
                    case "welcome":
                        _connecting = false;
                        // The server can reject a resume and allocate a new identity.
                        // Its sequence counter then starts at zero.
                        if (message.participantId != _expectedResumeId) _sequence = 0;
                        _expectedResumeId = null;
                        _welcomed = true;
                        _retryUntilUtc = default;
                        _retryAttempt = 0;
                        _resumeId = message.participantId;
                        _resumeToken = message.resumeToken;
                        IsHost = message.host;
                        // A host can rejoin with saved host credentials via the Join
                        // button. Restore room management rights for that connection.
                        if (IsHost) _hostTicket = _lastTicket;
                        ClearRemoteActors();
                        Status = "Connected to room " + ActiveRoom;
                        if (message.participants != null)
                        {
                            foreach (Participant participant in message.participants)
                                UpdateActor(participant.participantId, participant.name, participant.state);
                        }
                        break;
                    case "participant_joined":
                        UpdateActor(message.participantId, message.name, null);
                        break;
                    case "participant_left":
                        RemoveActor(message.participantId);
                        break;
                    case "state":
                        UpdateActor(message.participantId, null, message.state);
                        break;
                    case "room_closed":
                        bool wasHost = IsHost;
                        Disconnect();
                        _lastRoom = null;
                        _lastTicket = null;
                        Status = wasHost ? "Room closed" : "Room closed by host";
                        break;
                    case "error":
                        Status = "Session: " + message.code;
                        if (message.code != "host_already_connected") _retryUntilUtc = default;
                        break;
                    case "closed":
                        bool previouslyConnected = _welcomed;
                        string previousHostTicket = _hostTicket;
                        string previousInviteTicket = InviteTicket;
                        _connecting = false;
                        _closingRoom = false;
                        _welcomed = false;
                        // Keep the time of the first interruption. Failed retry attempts
                        // must not restart the server's 30-second resume grace period.
                        if (previouslyConnected || _retryUntilUtc == default)
                            _disconnectedAt = DateTime.UtcNow;
                        IsHost = false;
                        ActiveRoom = string.Empty;
                        InviteTicket = string.Empty;
                        _hostTicket = null;
                        _socket = null;
                        _cancellation = null;
                        if (Status.StartsWith("Connected", StringComparison.Ordinal)
                            || Status.StartsWith("Connecting", StringComparison.Ordinal)
                            || Status.StartsWith("Closing room", StringComparison.Ordinal))
                            Status = "Disconnected";
                        ClearRemoteActors();
                        if (previouslyConnected && _retryUntilUtc == default)
                            _retryUntilUtc = DateTime.UtcNow.AddSeconds(25);
                        if (_retryUntilUtc > DateTime.UtcNow && _lastEndpoint != null)
                        {
                            // Keep the hosting rights during an interrupted connection.
                            _hostTicket = previousHostTicket;
                            InviteTicket = previousInviteTicket;
                            _retryAtUtc = DateTime.UtcNow.AddSeconds(Mathf.Min(4f, 1f + _retryAttempt));
                            _retryAttempt++;
                            Status = "Connection lost; reconnecting...";
                        }
                        else _retryUntilUtc = default;
                        break;
                    case "ping":
                        SendLive("{\"type\":\"pong\"}");
                        break;
                }
            }
            foreach (RemoteActor actor in _actors.Values) actor.Interpolate(Time.unscaledDeltaTime);
            if (_retryUntilUtc != default && !_welcomed && !_connecting
                && DateTime.UtcNow >= _retryAtUtc)
            {
                if (DateTime.UtcNow < _retryUntilUtc) Reconnect();
                else
                {
                    _retryUntilUtc = default;
                    _hostTicket = null;
                    InviteTicket = string.Empty;
                    _resumeId = null;
                    _resumeToken = null;
                    Status = "Reconnect timed out";
                }
            }
            if (_welcomed && Time.unscaledTime >= _nextSend) SendAvatarState();
        }

        private void Reconnect()
        {
            // Join() normally clears the previous connection. Preserve the reconnect
            // window and host-only invitation details across this internal retry.
            DateTime deadline = _retryUntilUtc;
            DateTime firstDisconnection = _disconnectedAt;
            int attempt = _retryAttempt;
            string hostTicket = _hostTicket;
            string invitation = InviteTicket;
            Uri endpoint = _lastEndpoint;
            string room = _lastRoom, ticket = _lastTicket, name = _lastDisplayName;
            Join(endpoint.ToString(), room, ticket, name);
            _disconnectedAt = firstDisconnection;
            _retryUntilUtc = deadline;
            _retryAttempt = attempt;
            _hostTicket = hostTicket;
            InviteTicket = invitation;
            Status = "Reconnecting to room " + room + "...";
        }

        private void SendAvatarState()
        {
            _nextSend = Time.unscaledTime + SendInterval;
            LoadedAvatar avatar = _avatars != null ? _avatars.Current : null;
            if (avatar == null || avatar.Instance == null) return;
            Transform transform = avatar.Instance.transform;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            string avatarName = Path.GetFileNameWithoutExtension(avatar.SourcePath);
            if (_previousAvatar == avatarName && (position - _previousPosition).sqrMagnitude < 0.0001f
                && Quaternion.Angle(rotation, _previousRotation) < 0.1f) return;
            _previousAvatar = avatarName;
            _previousPosition = position;
            _previousRotation = rotation;
            _sequence++;
            SendLive(JsonUtility.ToJson(new StateMessage
            {
                type = "state", seq = _sequence,
                state = new ActorState
                {
                    avatar = avatarName,
                    position = new[] { position.x, position.y, position.z },
                    rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w },
                },
            }));
        }

        private void UpdateActor(string id, string name, ActorState state)
        {
            if (string.IsNullOrEmpty(id) || id == _resumeId) return;
            if (!_actors.TryGetValue(id, out RemoteActor actor))
            {
                actor = new RemoteActor(string.IsNullOrEmpty(name) ? "Remote participant" : name);
                _actors.Add(id, actor);
            }
            actor.Apply(name, state);
        }

        private void RemoveActor(string id)
        {
            if (id != null && _actors.TryGetValue(id, out RemoteActor actor))
            {
                actor.Dispose();
                _actors.Remove(id);
            }
        }

        private void ClearRemoteActors()
        {
            foreach (RemoteActor actor in _actors.Values) actor.Dispose();
            _actors.Clear();
        }

        public void Disconnect()
        {
            Interlocked.Increment(ref _generation);
            _welcomed = false;
            _connecting = false;
            _closingRoom = false;
            _retryUntilUtc = default;
            _retryAttempt = 0;
            _disconnectedAt = DateTime.UtcNow;
            _roomCreation?.Cancel();
            _roomCreation = null;
            // The network task may have disposed its CTS/socket before the main
            // thread consumes the queued close event.
            try { _cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            try { _socket?.Abort(); } catch (ObjectDisposedException) { }
            _cancellation = null;
            _socket = null;
            while (_incoming.TryDequeue(out _)) { }
            ClearRemoteActors();
            ActiveRoom = string.Empty;
            InviteTicket = string.Empty;
            IsHost = false;
            _hostTicket = null;
            _serverEndpoint = null;
            Status = "Disconnected";
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        private static bool TryWebSocketAddress(string address, out Uri endpoint)
        {
            endpoint = null;
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri value) || value.UserInfo.Length > 0)
                return false;
            string scheme = value.Scheme;
            if (scheme != "http" && scheme != "https" && scheme != "ws" && scheme != "wss") return false;
            var target = new UriBuilder(value)
            {
                Scheme = scheme == "https" || scheme == "wss" ? "wss" : "ws",
                Path = "/ws", Query = string.Empty, Fragment = string.Empty,
            };
            endpoint = target.Uri;
            return true;
        }

        private sealed class RemoteActor
        {
            private readonly GameObject _object;
            private Vector3 _targetPosition;
            private Quaternion _targetRotation = Quaternion.identity;
            private string _name;
            private string _avatar;
            private bool _hasPose;

            public string Description => string.IsNullOrEmpty(_avatar) ? _name : _name + " (" + _avatar + ")";

            public RemoteActor(string name)
            {
                _name = name;
                _object = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _object.name = "Session participant: " + name;
                _object.transform.localScale = new Vector3(0.35f, 0.75f, 0.35f);
                // Keep participants without tracking data in the roster without
                // placing an unrelated capsule at the world's origin.
                _object.SetActive(false);
                Collider collider = _object.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
                    else UnityEngine.Object.DestroyImmediate(collider);
                }
            }

            public void Apply(string name, ActorState state)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    _name = name;
                    _object.name = "Session participant: " + name;
                }
                if (state == null) return;
                if (!string.IsNullOrEmpty(state.avatar)) _avatar = state.avatar;
                bool firstPose = !_hasPose;
                if (state.position != null && state.position.Length == 3)
                {
                    _targetPosition = new Vector3(state.position[0], state.position[1], state.position[2]);
                    if (!_hasPose)
                    {
                        _object.transform.position = _targetPosition;
                        _hasPose = true;
                        _object.SetActive(true);
                    }
                }
                if (state.rotation != null && state.rotation.Length == 4)
                {
                    _targetRotation = new Quaternion(state.rotation[0], state.rotation[1], state.rotation[2], state.rotation[3]);
                    if (firstPose)
                        _object.transform.rotation = _targetRotation;
                }
            }

            public void Interpolate(float dt)
            {
                if (_object == null) return;
                float factor = 1f - Mathf.Exp(-12f * Mathf.Max(0f, dt));
                _object.transform.position = Vector3.Lerp(_object.transform.position, _targetPosition, factor);
                _object.transform.rotation = Quaternion.Slerp(_object.transform.rotation, _targetRotation, factor);
            }

            public void Dispose()
            {
                if (_object == null) return;
                if (Application.isPlaying) UnityEngine.Object.Destroy(_object);
                else UnityEngine.Object.DestroyImmediate(_object);
            }
        }

        [Serializable] private sealed class JoinMessage
        {
            public string type, room, ticket, name, resumeId, resumeToken;
            public int version;
        }
        [Serializable] private sealed class RoomReply { public string code, inviteTicket, hostTicket; }
        [Serializable] private sealed class StateMessage { public string type; public long seq; public ActorState state; }
        [Serializable] private sealed class ActorState { public string avatar; public float[] position, rotation; }
        [Serializable] private sealed class Participant
        {
            public string participantId, name;
            public ActorState state;
        }
        [Serializable] private sealed class Message
        {
            public string type, participantId, resumeToken, name, code;
            public bool host;
            public Participant[] participants;
            public ActorState state;
        }

        private readonly struct NetworkEvent
        {
            public readonly int Generation;
            public readonly string Payload;

            public NetworkEvent(int generation, string payload)
            {
                Generation = generation;
                Payload = payload;
            }
        }
    }
}
