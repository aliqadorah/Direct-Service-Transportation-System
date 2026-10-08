using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DirectService.Transportation.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Infrastructure.Security;

/// <summary>Configuration of URL tokens. <see cref="Key"/> is a base64 256-bit secret (user-secrets / secret store).</summary>
public sealed class UrlTokenOptions
{
    public const string Section = "UrlTokens";

    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Hashed URL identifiers (SEC-22, ADR-07): the record's random PublicId encrypted with AES-256 under a key
/// derived per entity type (HKDF-SHA256, info = entity name), base64url-encoded to 22 characters.
/// Deterministic (bookmarks keep working), unguessable, and a token of one entity type never opens another.
/// A tampered token decrypts to a random GUID that does not exist, so the caller answers 404.
/// </summary>
public sealed class UrlTokenService : IUrlTokenService
{
    private readonly byte[] _master;
    private readonly ConcurrentDictionary<Type, byte[]> _keys = new();

    public UrlTokenService(IOptions<UrlTokenOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Value.Key))
            throw new InvalidOperationException("UrlTokens:Key is not configured (base64, 32 bytes). Set it with dotnet user-secrets or an environment variable.");
        _master = Convert.FromBase64String(options.Value.Key);
        if (_master.Length != 32)
            throw new InvalidOperationException("UrlTokens:Key must be 32 bytes (base64).");
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
            info: Encoding.UTF8.GetBytes("DST-URL:" + t.Name)));
}
