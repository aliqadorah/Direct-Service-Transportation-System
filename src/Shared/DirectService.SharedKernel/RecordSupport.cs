using Microsoft.EntityFrameworkCore;

namespace DirectService.SharedKernel;

/// <summary>
/// The generic record helpers of every module's services (platform design a.5 "RecordSupport", Stage 2 session 13): a record by its URL
/// token, URL tokens of a type. The module helpers (Transportation's <c>MasterDataSupport</c>) forward to it.
/// </summary>
public sealed class RecordSupport(IAppDbContext db, IUrlTokenService tokens)
{
    public async Task<T> FindAsync<T>(string? token, CancellationToken ct) where T : AuditableEntity
    {
        var publicId = tokens.Decode<T>(token) ?? throw Fail.NotFound();
        return await db.Set<T>().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }

    public string Token<T>(Guid publicId) => tokens.Encode<T>(publicId);

    /// <summary>The PublicId of a token of type <typeparamref name="T"/>, or null when malformed or of another type.</summary>
    public Guid? Decode<T>(string? token) => tokens.Decode<T>(token);
}
