using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DirectService.Platform.Application.Identity;

/// <summary>
/// RFC 6238 time-based codes (6 digits, 30-second steps, HMAC-SHA1 — what authenticator apps and ASP.NET Core Identity use) for a key that
/// is not stored yet: the new key of "Change authenticator app" is confirmed with a code from the new app before it replaces the old one
/// (DT-210), and every stored authenticator code, through the replay-safe token provider (DT-212), which also needs the matched step.
/// The current step and one step either side are accepted (clock drift).
/// </summary>
public static class TotpCode
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static bool Verify(string base32Key, string? code, DateTimeOffset now) => MatchStep(base32Key, code, now) is not null;

    /// <summary>The time step (Unix seconds / 30) whose code this is — current, previous or next — or null. Every step is compared (no early exit).</summary>
    public static long? MatchStep(string base32Key, string? code, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 6 || !code.All(char.IsAsciiDigit)) return null;
        var key = FromBase32(base32Key);
        var step = now.ToUnixTimeSeconds() / 30;
        long? matched = null;
        for (var i = -1; i <= 1; i++)
        {
            if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Compute(key, step + i)), System.Text.Encoding.ASCII.GetBytes(code)))
                matched ??= step + i;
        }
        return matched;
    }

    /// <summary>The current time step.</summary>
    public static long StepOf(DateTimeOffset now) => now.ToUnixTimeSeconds() / 30;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 TOTP is defined with HMAC-SHA1 and authenticator apps use it (as ASP.NET Core Identity's authenticator provider does); HMAC-SHA1 is not weakened by SHA-1 collisions.")]
    private static string Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(key, counter, hash);
        var offset = hash[^1] & 0x0F;
        var value = (BinaryPrimitives.ReadInt32BigEndian(hash[offset..]) & 0x7FFFFFFF) % 1_000_000;
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] FromBase32(string input)
    {
        var clean = input.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var ch in clean)
        {
            var v = Alphabet.IndexOf(ch, StringComparison.Ordinal);
            if (v < 0) throw new FormatException("The key is not base32.");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. bytes];
    }
}
