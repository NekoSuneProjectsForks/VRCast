using System.Security.Cryptography;
using System.Text;

namespace VRCast.SessionServer;

public sealed class SessionConfig
{
    public const int ProtocolVersion = 1;
    public string BindAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 39580;
    public string AdminToken { get; init; } = string.Empty;
    public int MaxRooms { get; init; } = 100;
    public int MaxParticipantsPerRoom { get; init; } = 16;
    public int MaxMessagesPerSecond { get; init; } = 60;

    public static SessionConfig FromEnvironment()
    {
        static int ReadInt(string key, int fallback, int min, int max)
        {
            string? value = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(value) ? fallback
                : int.TryParse(value, out int parsed) && parsed >= min && parsed <= max
                    ? parsed : throw new ArgumentException($"Invalid {key} (expected {min}..{max}).");
        }

        string address = Environment.GetEnvironmentVariable("VRCAST_SESSION_BIND") ?? "127.0.0.1";
        if (!System.Net.IPAddress.TryParse(address, out _))
            throw new ArgumentException("VRCAST_SESSION_BIND must be a numeric IP address.");
        string token = Environment.GetEnvironmentVariable("VRCAST_SESSION_ADMIN_TOKEN") ?? string.Empty;
        if (token.Length < 24)
            throw new ArgumentException("Set VRCAST_SESSION_ADMIN_TOKEN to a random secret with at least 24 characters.");
        return new SessionConfig
        {
            BindAddress = address,
            Port = ReadInt("VRCAST_SESSION_PORT", 39580, 1, 65535),
            AdminToken = token,
            MaxRooms = ReadInt("VRCAST_SESSION_MAX_ROOMS", 100, 1, 1000),
            MaxParticipantsPerRoom = ReadInt("VRCAST_SESSION_MAX_PARTICIPANTS", 16, 2, 64),
            MaxMessagesPerSecond = ReadInt("VRCAST_SESSION_RATE_LIMIT", 60, 1, 120),
        };
    }

    public static bool SecretMatches(string? actual, string expected)
    {
        if (string.IsNullOrEmpty(actual)) return false;
        byte[] a = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string NewSecret(int bytes = 24) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));
}
