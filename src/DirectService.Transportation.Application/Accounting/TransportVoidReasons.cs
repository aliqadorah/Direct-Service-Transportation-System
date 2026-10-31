using DirectService.Accounting.Contracts.Posting;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// The void reasons of the transport documents (<see cref="IDocumentVoidReasonSource"/>, Stage 3 session 6c): trips, invoices, credit notes,
/// receipts, advances, settlements and partner payments, by the source type of their entries.
/// </summary>
public sealed class TransportVoidReasons(IAppDbContext db) : IDocumentVoidReasonSource
{
    private sealed record DocumentVoid(long Id, string? Reason);

    public IReadOnlyList<string> Types { get; } = ["Trip", "Invoice", "CreditNote", "Receipt", "Advance", "Settlement", "PartnerPayment"];

    public async Task<IReadOnlyDictionary<long, string?>> VoidReasonsAsync(string type, IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        IQueryable<DocumentVoid>? q = type switch
        {
            "Trip" => db.Set<Trip>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
            "Invoice" => db.Set<Invoice>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
            "CreditNote" => db.Set<CreditNote>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
            "Receipt" => db.Set<Receipt>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
            "Advance" => db.Set<Advance>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
            "Settlement" => db.Set<Settlement>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
            "PartnerPayment" => db.Set<PartnerPayment>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
            _ => null,
        };
        if (q is null) return new Dictionary<long, string?>();
        return (await q.AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).ToDictionary(d => d.Id, d => d.Reason);
    }
}
