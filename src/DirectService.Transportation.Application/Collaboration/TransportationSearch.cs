using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// The records of this application in the global search (FR-187, <see cref="ISearchProvider"/>, DT-313): receipt and document numbers,
/// drivers (name, national ID, plate, phone), customers and partners — in the order the search always listed them. Each record type appears
/// only with its view permission (DT-118). The accounting types split off with the Accounting module (Stage 3).
/// </summary>
public sealed class TransportationSearch(IAppDbContext db, IRequestContext ctx, MasterDataSupport support) : ISearchProvider
{
    private bool Any(params string[] keys) => keys.Any(ctx.HasPermission);

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (text, folded, compact, perType) = query;
        var hits = new List<SearchHit>();

        if (Any(PermissionKeys.TripsView))
        {
            hits.AddRange((await db.Set<Trip>().AsNoTracking().Where(t => t.ReceiptNo.Contains(text)).OrderByDescending(t => t.TripDate).Take(perType)
                    .Select(t => new { t.PublicId, t.ReceiptNo, t.TripDate, Customer = t.Customer.Name, t.Status }).ToListAsync(ct).ConfigureAwait(false))
                .Select(t => new SearchHit("trip", support.Token<Trip>(t.PublicId), t.ReceiptNo, $"{t.TripDate:yyyy-MM-dd} · {t.Customer}{(t.Status == "Void" ? " · Void" : string.Empty)}")));
        }
        if (Any(PermissionKeys.InvView))
        {
            hits.AddRange((await db.Set<Invoice>().AsNoTracking().Where(i => i.Number != null && i.Number.Contains(text)).OrderByDescending(i => i.InvoiceDate).Take(perType)
                    .Select(i => new { i.PublicId, i.Number, i.InvoiceDate, Customer = i.Customer.Name }).ToListAsync(ct).ConfigureAwait(false))
                .Select(i => new SearchHit("invoice", support.Token<Invoice>(i.PublicId), i.Number!, $"{i.InvoiceDate:yyyy-MM-dd} · {i.Customer}")));
            hits.AddRange((await db.Set<CreditNote>().AsNoTracking().Where(c => c.Number.Contains(text)).OrderByDescending(c => c.CreditDate).Take(perType)
                    .Select(c => new { c.PublicId, c.Number, c.CreditDate, Customer = c.Customer.Name }).ToListAsync(ct).ConfigureAwait(false))
                .Select(c => new SearchHit("credit-note", support.Token<CreditNote>(c.PublicId), c.Number, $"{c.CreditDate:yyyy-MM-dd} · {c.Customer}")));
        }
        if (Any(PermissionKeys.RcptManage, PermissionKeys.InvView))
        {
            hits.AddRange((await db.Set<Receipt>().AsNoTracking().Where(r => r.Number.Contains(text) || (r.Reference != null && r.Reference.Contains(text)))
                    .OrderByDescending(r => r.ReceiptDate).Take(perType).Select(r => new { r.PublicId, r.Number, r.ReceiptDate, Customer = r.Customer.Name }).ToListAsync(ct).ConfigureAwait(false))
                .Select(r => new SearchHit("receipt", support.Token<Receipt>(r.PublicId), r.Number, $"{r.ReceiptDate:yyyy-MM-dd} · {r.Customer}")));
        }
        if (Any(PermissionKeys.DrvView, PermissionKeys.DrvAdvance))
        {
            hits.AddRange((await db.Set<Advance>().AsNoTracking().Where(a => a.Number.Contains(text)).OrderByDescending(a => a.AdvanceDate).Take(perType)
                    .Select(a => new { a.PublicId, a.Number, a.AdvanceDate, Driver = a.Driver.FullName }).ToListAsync(ct).ConfigureAwait(false))
                .Select(a => new SearchHit("advance", support.Token<Advance>(a.PublicId), a.Number, $"{a.AdvanceDate:yyyy-MM-dd} · {a.Driver}")));
        }
        if (Any(PermissionKeys.DrvView, PermissionKeys.DrvSettle))
        {
            hits.AddRange((await db.Set<Settlement>().AsNoTracking().Where(s => s.Number.Contains(text)).OrderByDescending(s => s.SettlementDate).Take(perType)
                    .Select(s => new { s.PublicId, s.Number, s.SettlementDate, Driver = s.Driver.FullName }).ToListAsync(ct).ConfigureAwait(false))
                .Select(s => new SearchHit("settlement", support.Token<Settlement>(s.PublicId), s.Number, $"{s.SettlementDate:yyyy-MM-dd} · {s.Driver}")));
            hits.AddRange((await db.Set<PartnerPayment>().AsNoTracking().Where(p => p.Number.Contains(text)).OrderByDescending(p => p.PaymentDate).Take(perType)
                    .Select(p => new { p.PublicId, p.Number, p.PaymentDate, Partner = p.Partner.Name }).ToListAsync(ct).ConfigureAwait(false))
                .Select(p => new SearchHit("partner-payment", support.Token<PartnerPayment>(p.PublicId), p.Number, $"{p.PaymentDate:yyyy-MM-dd} · {p.Partner}")));
        }
        if (Any(PermissionKeys.AcctApar, PermissionKeys.AcctView))
        {
            hits.AddRange((await db.Set<ApArDocument>().AsNoTracking().Where(d => d.Number.Contains(text) || (d.Reference != null && d.Reference.Contains(text)))
                    .OrderByDescending(d => d.DocumentDate).Take(perType).Select(d => new { d.PublicId, d.Number, d.DocumentKind, d.DocumentDate, Party = d.Party.Name })
                    .ToListAsync(ct).ConfigureAwait(false))
                .Select(d => new SearchHit(d.DocumentKind == "SupplierBill" ? "supplier-bill" : "receivable", support.Token<ApArDocument>(d.PublicId), d.Number,
                    $"{d.DocumentDate:yyyy-MM-dd} · {d.Party}")));
            hits.AddRange((await db.Set<ApArParty>().AsNoTracking().Where(p => SqlText.Fold(p.Name)!.Contains(folded) || (p.TaxNumber != null && p.TaxNumber.Contains(compact)))
                    .OrderBy(p => p.Name).Take(perType).Select(p => new { p.PublicId, p.Name, p.PartyRole }).ToListAsync(ct).ConfigureAwait(false))
                .Select(p => new SearchHit("accounting-party", support.Token<ApArParty>(p.PublicId), p.Name, p.PartyRole)));
        }
        if (Any(PermissionKeys.AcctView, PermissionKeys.JeView))
        {
            // DT-254: the entry's reference or a line reference; a matching line reference is shown when the entry's does not match.
            hits.AddRange((await db.Set<JournalEntry>().AsNoTracking()
                    .Where(e => e.Number.Contains(text) || (e.Reference != null && e.Reference.Contains(text)) || e.Lines.Any(l => l.Reference != null && l.Reference.Contains(text)))
                    .OrderByDescending(e => e.EntryDate).Take(perType)
                    .Select(e => new
                    {
                        e.PublicId, e.Number, e.EntryDate, e.Description,
                        Reference = e.Reference != null && e.Reference.Contains(text) ? e.Reference
                            : e.Lines.Where(l => l.Reference != null && l.Reference.Contains(text)).Select(l => l.Reference).FirstOrDefault() ?? e.Reference,
                    }).ToListAsync(ct).ConfigureAwait(false))
                .Select(e => new SearchHit("journal-entry", support.Token<JournalEntry>(e.PublicId), e.Number,
                    e.Reference is null ? $"{e.EntryDate:yyyy-MM-dd} · {e.Description}" : $"{e.EntryDate:yyyy-MM-dd} · {e.Reference} · {e.Description}")));
        }
        if (Any(PermissionKeys.DrvView, PermissionKeys.MastersManage))
        {
            // National ID only with drv.view (personal data, DT-36).
            var withId = ctx.HasPermission(PermissionKeys.DrvView);
            hits.AddRange((await db.Set<Driver>().AsNoTracking()
                    .Where(d => SqlText.Fold(d.FullName)!.Contains(folded) || d.PlateNumber.Contains(compact) || d.Phone1.Contains(compact) || (d.Phone2 != null && d.Phone2.Contains(compact))
                        || (withId && d.NationalId.Contains(compact)))
                    .OrderBy(d => d.FullName).Take(perType).Select(d => new { d.PublicId, d.FullName, d.PlateNumber, d.Status }).ToListAsync(ct).ConfigureAwait(false))
                .Select(d => new SearchHit("driver", support.Token<Driver>(d.PublicId), d.FullName, d.PlateNumber + (d.Status == "Archived" ? " · Archived" : string.Empty))));
            hits.AddRange((await db.Set<Partner>().AsNoTracking().Where(p => SqlText.Fold(p.Name)!.Contains(folded) || (p.Phone != null && p.Phone.Contains(compact)))
                    .OrderBy(p => p.Name).Take(perType).Select(p => new { p.PublicId, p.Name, p.Status }).ToListAsync(ct).ConfigureAwait(false))
                .Select(p => new SearchHit("partner", support.Token<Partner>(p.PublicId), p.Name, p.Status == "Archived" ? "Archived" : null)));
        }
        // Customers are readable by every member (DT-36).
        hits.AddRange((await db.Set<Customer>().AsNoTracking().Where(c => SqlText.Fold(c.Name)!.Contains(folded) || (c.TaxNumber != null && c.TaxNumber.Contains(compact))
                    || (c.Phone != null && c.Phone.Contains(compact)))
                .OrderBy(c => c.Name).Take(perType).Select(c => new { c.PublicId, c.Name, c.City, c.Status }).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => new SearchHit("customer", support.Token<Customer>(c.PublicId), c.Name, c.City + (c.Status == "Archived" ? " · Archived" : string.Empty))));
        return hits;
    }
}
