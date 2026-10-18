using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace VRCast.SessionServer;

/// <summary>Small, ephemeral, headless rooms. A room owns its state; clients only own their actor.</summary>
public sealed class SessionHub
{
    private readonly ConcurrentDictionary<string, SessionRoom> _rooms = new(StringComparer.Ordinal);
    private readonly SessionConfig _options;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public SessionHub(SessionConfig options) => _options = options;

    public object Status => new
    {
        protocolVersion = SessionConfig.ProtocolVersion,
        rooms = _rooms.Count,
        participants = _rooms.Values.Sum(room => { lock (room.Gate) return room.Members.Values.Count(p => p.Active); }),
    };

    public object? CreateRoom()
    {
        if (_rooms.Count >= _options.MaxRooms) return null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            byte[] random = System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);
            string code = new(random.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
            var room = new SessionRoom(code);
            if (_rooms.TryAdd(code, room))
                return new { code, inviteTicket = room.InviteTicket, hostTicket = room.HostTicket,
                    protocolVersion = SessionConfig.ProtocolVersion };
        }
        return null;
    }

    public bool DeleteRoom(string code, string? ticket)
    {
        if (!_rooms.TryGetValue(code.ToUpperInvariant(), out SessionRoom? room)) return false;
        if (!SessionConfig.SecretMatches(ticket, room.HostTicket)
            && !SessionConfig.SecretMatches(ticket, _options.AdminToken)) return false;
        lock (room.Gate)
        {
            if (room.IsRevoked || !_rooms.TryRemove(room.Code, out _)) return false;
            room.IsRevoked = true;
            foreach (SessionMember member in room.Members.Values)
            {
                // Let each connection's single writer flush the notification before
                // closing its WebSocket. Abort() here discards queued notifications.
                member.RoomClosed = true;
                member.Send(new { type = "room_closed" });
                member.Outbox.Writer.TryComplete();
            }
        }
        return true;
    }

    public bool TryJoin(JoinRequest join, WebSocket socket, out SessionRoom? room,
        out SessionMember? member, out string reason)
    {
        member = null;
        reason = "invalid_room_or_ticket";
        if (!_rooms.TryGetValue(join.Room.ToUpperInvariant(), out room)) return false;
        lock (room.Gate)
        {
            // A concurrent join may already hold a reference to a removed room.
            if (room.IsRevoked) return false;
            bool isHost = SessionConfig.SecretMatches(join.Ticket, room.HostTicket);
            if (!isHost && !SessionConfig.SecretMatches(join.Ticket, room.InviteTicket)) return false;

            SessionMember? old = null;
            if (join.ResumeId != null && room.Members.TryGetValue(join.ResumeId, out SessionMember? candidate)
                && !candidate.Active && DateTime.UtcNow - candidate.LastSeenUtc < TimeSpan.FromSeconds(30)
                && SessionConfig.SecretMatches(join.ResumeToken, candidate.ResumeToken)
                && candidate.IsHost == isHost)
                old = candidate;

            if (isHost && room.Members.Values.Any(m => m.IsHost && m.Active))
            {
                reason = "host_already_connected";
                return false;
            }
            if (old == null && room.Members.Values.Count(m => m.Active) >= _options.MaxParticipantsPerRoom)
            {
                reason = "room_full";
                return false;
            }

            if (old != null)
            {
                member = old.Resume(socket, join.Name);
                room.Members[member.Id] = member;
            }
            else
            {
                member = new SessionMember(Guid.NewGuid().ToString("N"), join.Name, isHost, socket);
                room.Members.Add(member.Id, member);
            }

            // Snapshot first, then queued future events. Messages for this connection remain ordered.
            SessionMember joined = member;
            member.Send(new
            {
                type = "welcome", protocolVersion = SessionConfig.ProtocolVersion, room = room.Code,
                participantId = member.Id, resumeToken = member.ResumeToken, host = member.IsHost,
                participants = room.Members.Values.Where(m => m.Active && m != joined)
                    .Select(m => new { participantId = m.Id, name = m.Name, host = m.IsHost,
                        seq = m.LastSequence, state = m.LastState }).ToArray(),
            });
            room.Broadcast(new { type = "participant_joined", participantId = member.Id,
                name = member.Name, host = member.IsHost }, member);
            return true;
        }
    }

    public string? Receive(SessionRoom room, SessionMember sender, JsonElement message)
    {
        lock (room.Gate)
        {
            if (room.IsRevoked || !sender.Active || sender.RoomClosed || !ReferenceEquals(room.Members.GetValueOrDefault(sender.Id), sender))
                return "disconnected";
            DateTime now = DateTime.UtcNow;
            sender.LastSeenUtc = now;
            if (now - sender.RateWindow >= TimeSpan.FromSeconds(1))
            {
                sender.RateWindow = now;
                sender.MessageCount = 0;
            }
            if (++sender.MessageCount > _options.MaxMessagesPerSecond) return "rate_limited";
            if (SessionProtocol.HasType(message, "pong") || SessionProtocol.HasType(message, "ping"))
            {
                if (SessionProtocol.HasType(message, "ping")) sender.Send(new { type = "pong" });
                return null;
            }
            if (SessionProtocol.TryState(message, out long sequence, out JsonElement state))
            {
                if (sequence <= sender.LastSequence) return "stale_sequence";
                sender.LastSequence = sequence;
                sender.LastState = state;
                room.Broadcast(new { type = "state", participantId = sender.Id, seq = sequence, state }, sender);
                return null;
            }
            if (SessionProtocol.TrySceneEvent(message, out sequence, out string name))
            {
                if (!sender.IsHost) return "host_only";
                if (sequence <= sender.LastSequence) return "stale_sequence";
                sender.LastSequence = sequence;
                room.Broadcast(new { type = "scene", participantId = sender.Id, seq = sequence, name }, sender);
                return null;
            }
            return "invalid_message";
        }
    }

    public void Leave(SessionRoom room, SessionMember member)
    {
        lock (room.Gate)
        {
            if (!member.Active || !ReferenceEquals(room.Members.GetValueOrDefault(member.Id), member)) return;
            member.Active = false;
            member.LastSeenUtc = DateTime.UtcNow;
            member.Outbox.Writer.TryComplete();
            room.Broadcast(new { type = "participant_left", participantId = member.Id }, member);
        }
    }

    public void Cleanup()
    {
        DateTime now = DateTime.UtcNow;
        foreach (SessionRoom room in _rooms.Values)
        {
            lock (room.Gate)
            {
                foreach (SessionMember member in room.Members.Values.ToArray())
                {
                    if (member.Active && now - member.LastSeenUtc > TimeSpan.FromSeconds(45))
                    {
                        member.Socket?.Abort();
                        Leave(room, member);
                    }
                    if (!member.Active && now - member.LastSeenUtc > TimeSpan.FromSeconds(30))
                        room.Members.Remove(member.Id);
                }
                if (room.Members.Count == 0 && now - room.CreatedUtc > TimeSpan.FromMinutes(10)
                    && !room.IsRevoked && _rooms.TryRemove(room.Code, out _))
                {
                    // A join may have obtained the reference before removal.
                    // Invalidate it while holding the same gate used by TryJoin.
                    room.IsRevoked = true;
                }
            }
        }
    }
}

public sealed class SessionRoom
{
    public string Code { get; }
    public string InviteTicket { get; } = SessionConfig.NewSecret();
    public string HostTicket { get; } = SessionConfig.NewSecret();
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; }
    public object Gate { get; } = new();
    public Dictionary<string, SessionMember> Members { get; } = new(StringComparer.Ordinal);

    public SessionRoom(string code) => Code = code;

    public void Broadcast(object message, SessionMember? except = null)
    {
        string payload = JsonSerializer.Serialize(message);
        foreach (SessionMember member in Members.Values)
            if (member.Active && member != except) member.Outbox.Writer.TryWrite(payload);
    }
}

public sealed class SessionMember
{
    public string Id { get; }
    public string Name { get; }
    public bool IsHost { get; }
    public string ResumeToken { get; } = SessionConfig.NewSecret();
    public WebSocket? Socket { get; }
    public bool Active { get; set; } = true;
    public bool RoomClosed { get; set; }
    public long LastSequence { get; set; }
    public JsonElement? LastState { get; set; }
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime RateWindow { get; set; } = DateTime.UtcNow;
    public int MessageCount { get; set; }
    public Channel<string> Outbox { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
    {
        SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest,
    });

    public SessionMember(string id, string name, bool isHost, WebSocket socket)
    {
        Id = id;
        Name = name;
        IsHost = isHost;
        Socket = socket;
    }

    public SessionMember Resume(WebSocket socket, string name) => new(Id, name, IsHost, socket)
    {
        LastSequence = LastSequence,
        LastState = LastState,
    };

    public void Send(object message) => Outbox.Writer.TryWrite(JsonSerializer.Serialize(message));
}
