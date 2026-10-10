using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// Global search (FR-187, Ctrl+K): receipt and document numbers, drivers (name, national ID, plate, phone), customers and partners, within the
/// selected company. Arabic spelling variants are tolerated (hamza forms of alef, taa marbuta, alef maqsura folded on both sides; diacritics
/// and case by the collation), digits are matched in ASCII. Each record type appears only with its view permission (DT-118); at most
/// <see cref="PerType"/> hits per type.
/// </summary>
public sealed class GlobalSearchService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support)
{
    public const int PerType = 5;

    private bool Any(params string[] keys) => keys.Any(ctx.HasPermission);

    public async Task<SearchResponse> SearchAsync(string? q, CancellationToken ct)
    {
        var text = TextNormalizer.CleanText(TextNormalizer.ToAsciiDigits(q));
        if (text is null || text.Length < 2) throw Fail.Field("q", ErrorCodes.ValidationRange, "q");
        if (text.Length > 100) text = text[..100];
        var folded = SqlText.FoldText(text);
        var compact = MasterDataSupport.Compact(text) ?? text;
        var hits = new List<SearchHit>();

        if (Any(PermissionKeys.TripsView))
        {
            hits.AddRange((await db.Set<Trip>().AsNoTracking().Where(t => t.ReceiptNo.Contains(text)).OrderByDescending(t => t.TripDate).Take(PerType)
                    .Select(t => new { t.PublicId, t.ReceiptNo, t.TripDate, Customer = t.Customer.Name, t.Status }).ToListAsync(ct).ConfigureAwait(false))
                .Select(t => new SearchHit("trip", support.Token<Trip>(t.PublicId), t.ReceiptNo, $"{t.TripDate:yyyy-MM-dd} · {t.Customer}{(t.Status == "Void" ? " · Void" : string.Empty)}")));
        }
        if (Any(PermissionKeys.InvView))
        {
            hits.AddRange((await db.Set<Invoice>().AsNoTracking().Where(i => i.Number != null && i.Number.Contains(text)).OrderByDescending(i => i.InvoiceDate).Take(PerType)
                    .Select(i => new { i.PublicId, i.Number, i.InvoiceDate, Customer = i.Customer.Name }).ToListAsync(ct).ConfigureAwait(false))
                .Select(i => new SearchHit("invoice", support.Token<Invoice>(i.PublicId), i.Number!, $"{i.InvoiceDate:yyyy-MM-dd} · {i.Customer}")));
            hits.AddRange((await db.Set<CreditNote>().AsNoTracking().Where(c => c.Number.Contains(text)).OrderByDescending(c => c.CreditDate).Take(PerType)
                    .Select(c => new { c.PublicId, c.Number, c.CreditDate, Customer = c.Customer.Name }).ToListAsync(ct).ConfigureAwait(false))
                .Select(c => new SearchHit("credit-note", support.Token<CreditNote>(c.PublicId), c.Number, $"{c.CreditDate:yyyy-MM-dd} · {c.Customer}")));
        }
        if (Any(PermissionKeys.RcptManage, PermissionKeys.InvView))
        {
            hits.AddRange((await db.Set<Receipt>().AsNoTracking().Where(r => r.Number.Contains(text) || (r.Reference != null && r.Reference.Contains(text)))
                    .OrderByDescending(r => r.ReceiptDate).Take(PerType).Select(r => new { r.PublicId, r.Number, r.ReceiptDate, Customer = r.Customer.Name }).ToListAsync(ct).ConfigureAwait(false))
                .Select(r => new SearchHit("receipt", support.Token<Receipt>(r.PublicId), r.Number, $"{r.ReceiptDate:yyyy-MM-dd} · {r.Customer}")));
        }
        if (Any(PermissionKeys.DrvView, PermissionKeys.DrvAdvance))
        {
            hits.AddRange((await db.Set<Advance>().AsNoTracking().Where(a => a.Number.Contains(text)).OrderByDescending(a => a.AdvanceDate).Take(PerType)
                    .Select(a => new { a.PublicId, a.Number, a.AdvanceDate, Driver = a.Driver.FullName }).ToListAsync(ct).ConfigureAwait(false))
                .Select(a => new SearchHit("advance", support.Token<Advance>(a.PublicId), a.Number, $"{a.AdvanceDate:yyyy-MM-dd} · {a.Driver}")));
        }
        if (Any(PermissionKeys.DrvView, PermissionKeys.DrvSettle))
        {
            hits.AddRange((await db.Set<Settlement>().AsNoTracking().Where(s => s.Number.Contains(text)).OrderByDescending(s => s.SettlementDate).Take(PerType)
                    .Select(s => new { s.PublicId, s.Number, s.SettlementDate, Driver = s.Driver.FullName }).ToListAsync(ct).ConfigureAwait(false))
                .Select(s => new SearchHit("settlement", support.Token<Settlement>(s.PublicId), s.Number, $"{s.SettlementDate:yyyy-MM-dd} · {s.Driver}")));
            hits.AddRange((await db.Set<PartnerPayment>().AsNoTracking().Where(p => p.Number.Contains(text)).OrderByDescending(p => p.PaymentDate).Take(PerType)
                    .Select(p => new { p.PublicId, p.Number, p.PaymentDate, Partner = p.Partner.Name }).ToListAsync(ct).ConfigureAwait(false))
                .Select(p => new SearchHit("partner-payment", support.Token<PartnerPayment>(p.PublicId), p.Number, $"{p.PaymentDate:yyyy-MM-dd} · {p.Partner}")));
        }
        if (Any(PermissionKeys.AcctApar, PermissionKeys.AcctView))
        {
            hits.AddRange((await db.Set<ApArDocument>().AsNoTracking().Where(d => d.Number.Contains(text) || (d.Reference != null && d.Reference.Contains(text)))
                    .OrderByDescending(d => d.DocumentDate).Take(PerType).Select(d => new { d.PublicId, d.Number, d.DocumentKind, d.DocumentDate, Party = d.Party.Name })
                    .ToListAsync(ct).ConfigureAwait(false))
                .Select(d => new SearchHit(d.DocumentKind == "SupplierBill" ? "supplier-bill" : "receivable", support.Token<ApArDocument>(d.PublicId), d.Number,
                    $"{d.DocumentDate:yyyy-MM-dd} · {d.Party}")));
            hits.AddRange((await db.Set<ApArParty>().AsNoTracking().Where(p => SqlText.Fold(p.Name)!.Contains(folded) || (p.TaxNumber != null && p.TaxNumber.Contains(compact)))
                    .OrderBy(p => p.Name).Take(PerType).Select(p => new { p.PublicId, p.Name, p.PartyRole }).ToListAsync(ct).ConfigureAwait(false))
                .Select(p => new SearchHit("accounting-party", support.Token<ApArParty>(p.PublicId), p.Name, p.PartyRole)));
        }
        if (Any(PermissionKeys.AcctView, PermissionKeys.JeView))
        {
            hits.AddRange((await db.Set<JournalEntry>().AsNoTracking().Where(e => e.Number.Contains(text)).OrderByDescending(e => e.EntryDate).Take(PerType)
                    .Select(e => new { e.PublicId, e.Number, e.EntryDate, e.Description }).ToListAsync(ct).ConfigureAwait(false))
                .Select(e => new SearchHit("journal-entry", support.Token<JournalEntry>(e.PublicId), e.Number, $"{e.EntryDate:yyyy-MM-dd} · {e.Description}")));
        }
        if (Any(PermissionKeys.DrvView, PermissionKeys.MastersManage))
        {
            // National ID only with drv.view (personal data, DT-36).
            var withId = ctx.HasPermission(PermissionKeys.DrvView);
            hits.AddRange((await db.Set<Driver>().AsNoTracking()
                    .Where(d => SqlText.Fold(d.FullName)!.Contains(folded) || d.PlateNumber.Contains(compact) || d.Phone1.Contains(compact) || (d.Phone2 != null && d.Phone2.Contains(compact))
                        || (withId && d.NationalId.Contains(compact)))
                    .OrderBy(d => d.FullName).Take(PerType).Select(d => new { d.PublicId, d.FullName, d.PlateNumber, d.Status }).ToListAsync(ct).ConfigureAwait(false))
                .Select(d => new SearchHit("driver", support.Token<Driver>(d.PublicId), d.FullName, d.PlateNumber + (d.Status == "Archived" ? " · Archived" : string.Empty))));
            hits.AddRange((await db.Set<Partner>().AsNoTracking().Where(p => SqlText.Fold(p.Name)!.Contains(folded) || (p.Phone != null && p.Phone.Contains(compact)))
                    .OrderBy(p => p.Name).Take(PerType).Select(p => new { p.PublicId, p.Name, p.Status }).ToListAsync(ct).ConfigureAwait(false))
                .Select(p => new SearchHit("partner", support.Token<Partner>(p.PublicId), p.Name, p.Status == "Archived" ? "Archived" : null)));
        }
        // Customers are readable by every member (DT-36).
        hits.AddRange((await db.Set<Customer>().AsNoTracking().Where(c => SqlText.Fold(c.Name)!.Contains(folded) || (c.TaxNumber != null && c.TaxNumber.Contains(compact))
                    || (c.Phone != null && c.Phone.Contains(compact)))
                .OrderBy(c => c.Name).Take(PerType).Select(c => new { c.PublicId, c.Name, c.City, c.Status }).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => new SearchHit("customer", support.Token<Customer>(c.PublicId), c.Name, c.City + (c.Status == "Archived" ? " · Archived" : string.Empty))));
        return new SearchResponse(text, hits);
    }
}
