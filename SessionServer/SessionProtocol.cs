using System.Text.Json;

namespace VRCast.SessionServer;

public static class SessionProtocol
{
    public const int MaxFrameBytes = 16384;

    public static bool TryJoin(JsonElement json, out JoinRequest request)
    {
        request = default!;
        if (!HasType(json, "join") || !TryString(json, "room", 6, 12, out string? room)
            || !TryString(json, "ticket", 24, 256, out string? ticket)
            || !TryString(json, "name", 1, 48, out string? name)) return false;
        int version = ReadInt(json, "version", -1);
        if (version != SessionConfig.ProtocolVersion) return false;
        string? resumeId = GetOptionalString(json, "resumeId", 64);
        string? resumeToken = GetOptionalString(json, "resumeToken", 256);
        request = new JoinRequest(room!, ticket!, name!, resumeId, resumeToken);
        return true;
    }

    public static bool TryState(JsonElement message, out long sequence, out JsonElement state)
    {
        sequence = 0;
        state = default;
        if (!HasType(message, "state") || !TrySequence(message, out sequence)
            || !message.TryGetProperty("state", out state)
            || state.ValueKind != JsonValueKind.Object) return false;

        // Only declared avatar state may cross the room boundary. No arbitrary filenames or commands.
        foreach (JsonProperty field in state.EnumerateObject())
        {
            switch (field.Name)
            {
                case "avatar":
                case "motion":
                    if (field.Value.ValueKind != JsonValueKind.String || field.Value.GetString()!.Length > 128) return false;
                    break;
                case "position":
                    if (!IsVector(field.Value, 3, 100000)) return false;
                    break;
                case "rotation":
                    if (!IsVector(field.Value, 4, 2)) return false;
                    break;
                case "expressions":
                    if (!IsParameters(field.Value)) return false;
                    break;
                default:
                    return false;
            }
        }

        state = state.Clone();
        return true;
    }

    public static bool TrySceneEvent(JsonElement message, out long sequence, out string name)
    {
        sequence = 0;
        name = string.Empty;
        if (!HasType(message, "scene") || !TrySequence(message, out sequence)
            || !TryString(message, "name", 1, 64, out string? value)) return false;
        // Scene events are intent-only; their names cannot invoke arbitrary code on receivers.
        if (!value!.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')) return false;
        name = value!;
        return true;
    }

    public static bool HasType(JsonElement value, string type) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("type", out JsonElement element)
        && element.ValueKind == JsonValueKind.String && element.GetString() == type;

    private static bool TrySequence(JsonElement message, out long sequence)
    {
        sequence = 0;
        return message.TryGetProperty("seq", out JsonElement seq)
            && seq.ValueKind == JsonValueKind.Number && seq.TryGetInt64(out sequence) && sequence > 0;
    }

    private static int ReadInt(JsonElement value, string key, int fallback) =>
        value.TryGetProperty(key, out JsonElement member) && member.TryGetInt32(out int result) ? result : fallback;

    private static bool TryString(JsonElement value, string key, int min, int max, out string? result)
    {
        result = null;
        if (!value.TryGetProperty(key, out JsonElement member) || member.ValueKind != JsonValueKind.String) return false;
        result = member.GetString();
        return result != null && result.Length >= min && result.Length <= max && !result.Any(char.IsControl);
    }

    private static string? GetOptionalString(JsonElement value, string key, int max) =>
        value.TryGetProperty(key, out JsonElement element) && element.ValueKind == JsonValueKind.String
            && element.GetString()!.Length <= max ? element.GetString() : null;

    private static bool IsVector(JsonElement value, int count, double limit)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count) return false;
        foreach (JsonElement element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double number)
                || !double.IsFinite(number) || Math.Abs(number) > limit) return false;
        }
        return true;
    }

    private static bool IsParameters(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        int count = 0;
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (++count > 64 || property.Name.Length is < 1 or > 64
                || !property.Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
                || property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetDouble(out double number)
                || !double.IsFinite(number) || number < 0 || number > 1) return false;
        }
        return true;
    }
}

public sealed record JoinRequest(string Room, string Ticket, string Name, string? ResumeId, string? ResumeToken);
