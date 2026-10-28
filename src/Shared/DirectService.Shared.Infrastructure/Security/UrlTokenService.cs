using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DirectService.Shared.Infrastructure.Security;

/// <summary>Configuration of URL tokens. <see cref="Key"/> is a base64 256-bit secret (user-secrets / secret store).</summary>
public sealed class UrlTokenOptions
{
    public const string Section = "UrlTokens";

    public string Key { get; set; } = string.Empty;

    /// <summary>What is wrong with a configured key, with how to set a correct one; null when the key is a base64 string of 32 bytes.</summary>
    public static string? Problem(string? key)
    {
        string what;
        if (string.IsNullOrWhiteSpace(key)) what = "UrlTokens:Key is not set.";
        else
        {
            var bytes = new byte[key.Length];
            if (!Convert.TryFromBase64String(key.Trim(), bytes, out var length)) what = "UrlTokens:Key is not valid base64.";
            else if (length != 32) what = $"UrlTokens:Key is {length} bytes; it must be exactly 32 bytes (256 bits), which is 44 base64 characters ending in '='.";
            else return null;
        }
        return what + " Create a key in PowerShell with "
            + "$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b) "
            + "(or in Git Bash: openssl rand -base64 32). Development: dotnet user-secrets set UrlTokens:Key \"<key>\" --project src/DirectService.Transportation.Web. "
            + "Production: the environment variable UrlTokens__Key or the host's secret store. Never put it in appsettings.json. "
            + "Keep the same key afterwards: changing it breaks every saved link. See docs/step9/Setup_and_Run.md section 4.";
    }
}

/// <summary>Checks the key when the host starts (instead of failing the first request that needs a URL token).</summary>
public sealed class UrlTokenOptionsValidator : IValidateOptions<UrlTokenOptions>
{
    public ValidateOptionsResult Validate(string? name, UrlTokenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return UrlTokenOptions.Problem(options.Key) is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Hashed URL identifiers (SEC-22, ADR-07): the record's random PublicId encrypted with AES-256 under a key
/// derived per entity type (HKDF-SHA256, info = <see cref="InfoPrefix"/> + the entity's pinned <see cref="TokenLabelAttribute"/>, platform
/// design §e, E-3 — never the class name, so moving or renaming a class keeps every link), base64url-encoded to 22 characters.
/// Deterministic (bookmarks keep working), unguessable, and a token of one entity type never opens another.
/// A tampered token decrypts to a random GUID that does not exist, so the caller answers 404.
/// </summary>
public sealed class UrlTokenService : IUrlTokenService
{
    /// <summary>HKDF info prefix of the per-type key (stored in every URL id; never change).</summary>
    public const string InfoPrefix = "DST-URL:";

    private readonly byte[] _master;
    private readonly ConcurrentDictionary<Type, byte[]> _keys = new();

    public UrlTokenService(IOptions<UrlTokenOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (UrlTokenOptions.Problem(options.Value.Key) is { } problem) throw new InvalidOperationException(problem);
        _master = Convert.FromBase64String(options.Value.Key.Trim());
    }

    public string Encode<TEntity>(Guid publicId) => Encode(typeof(TEntity), publicId);

    public Guid? Decode<TEntity>(string? token) => Decode(typeof(TEntity), token);

    public string Encode(Type entityType, Guid publicId)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        Span<byte> block = stackalloc byte[16];
        publicId.TryWriteBytes(block);
        using var aes = Aes.Create();
        aes.Key = KeyFor(entityType);
        var cipher = aes.EncryptEcb(block, PaddingMode.None);
        return Base64Url.EncodeToString(cipher);
    }

    public Guid? Decode(Type entityType, string? token)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        if (token is null || token.Length != 22) return null;
        Span<byte> cipher = stackalloc byte[16];
        if (Base64Url.DecodeFromChars(token, cipher, out _, out var written) != System.Buffers.OperationStatus.Done || written != 16)
            return null;
        using var aes = Aes.Create();
        aes.Key = KeyFor(entityType);
        var plain = aes.DecryptEcb(cipher.ToArray(), PaddingMode.None);
        var id = new Guid(plain);
        return id == Guid.Empty ? null : id;
    }

    private byte[] KeyFor(Type type) =>
        _keys.GetOrAdd(type, t => HKDF.DeriveKey(HashAlgorithmName.SHA256, _master, 32, salt: [],
            info: Encoding.UTF8.GetBytes(InfoPrefix + StoredNames.TokenLabelOf(t))));
}
