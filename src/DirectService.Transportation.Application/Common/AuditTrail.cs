using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Domain.Core;

namespace DirectService.Transportation.Application.Common;

/// <summary>
/// Audit rows for business events that change no column (e.g. every print, FR-058; reprints marked Copy, FR-090): added to
/// core.AuditLogs with the acting user, company and IP, saved with the caller's next SaveChanges.
/// </summary>
public sealed class AuditTrail(IAppDbContext db, IRequestContext ctx, PrintMode print)
{
    private static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <param name="tenantId">The tenant the row belongs to when the actor has none (platform staff, e.g. marking a refund, DT-185).</param>
    public void Record(string entityType, long entityId, string action, string? reason = null, IReadOnlyDictionary<string, object?>? values = null, long? tenantId = null)
    {
        // A screen preview is not a print: the printout or PDF that follows is logged (and marked Copy) as the print (FR-058, FR-090).
        if (print.Preview && action == "Print") return;
        var newValues = new Dictionary<string, object?>(values ?? new Dictionary<string, object?>());
        if (reason is not null) newValues["_reason"] = reason;
        db.Set<AuditLog>().Add(new AuditLog
        {
            TenantId = tenantId ?? ctx.TenantId,
            CompanyId = ctx.CompanyId,
            UserId = ctx.UserId,
            OccurredAt = DateTime.UtcNow,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            NewValues = newValues.Count == 0 ? null : JsonSerializer.Serialize(newValues, Readable),
            IpAddress = ctx.IpAddress,
        });
    }
}
