using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.EInvoicing;

/// <summary>
/// Builds the market-neutral <see cref="EInvoiceDocument"/> of an approved invoice or a credit note from current data (FR-060), so a
/// rejected document whose customer or settings were corrected is rebuilt on resubmission (D-04). The national formats carry
/// discounts per line and tax per line (JoFotara), while the platform stores one discount and one tax per document (8.3): the stored
/// net subtotal, discount and fees are split over the lines (largest remainder, exact sums), fees become their own line, each line's
/// tax is rounded on its net amount and the rounding difference to the stored tax is spread one smallest unit per line (DT-82), so
/// the document always totals exactly what was posted and printed.
/// </summary>
public sealed class EInvoiceDocumentBuilder(IAppDbContext db)
{
    public async Task<EInvoiceDocument> InvoiceAsync(Invoice inv, long icv, EInvoiceSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inv);
        ArgumentNullException.ThrowIfNull(settings);
        var (company, buyer) = await PartiesAsync(inv.CustomerId, ct).ConfigureAwait(false);
        var lines = await db.Set<InvoiceLine>().AsNoTracking().Where(l => l.InvoiceId == inv.Id).OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
            .Select(l => new SourceLine(l.Description, l.Quantity, l.UnitPrice, l.Amount, l.Trip != null ? l.Trip.ReceiptNo : null,
                l.Trip != null ? l.Trip.LoadingSite.Name : null, l.Trip != null ? l.Trip.UnloadingSite.Name : null, l.Trip != null ? l.Trip.Material.Name : null))
            .ToListAsync(ct).ConfigureAwait(false);
        var built = Allocate(lines.Select(l => (Describe(l), l.Amount)).ToList(), inv.Subtotal, inv.Discount, inv.ExtraFees, inv.ExtraFeesDescription, inv.Tax, inv.TaxRate,
            company.CurrencyDecimals);
        return Document(EInvoiceCodes.Invoice, inv.Number!, inv.PublicId, inv.InvoiceDate, icv, inv.DueDate > inv.InvoiceDate, settings, company, buyer, built, inv.Total, null, null);   // internal notes are not sent (review L3)
    }

    public async Task<EInvoiceDocument> CreditNoteAsync(CreditNote cn, Invoice inv, Guid originalUuid, long icv, EInvoiceSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cn);
        ArgumentNullException.ThrowIfNull(inv);
        ArgumentNullException.ThrowIfNull(settings);
        var (company, buyer) = await PartiesAsync(cn.CustomerId, ct).ConfigureAwait(false);
        var lines = await db.Set<CreditNoteLine>().AsNoTracking().Where(l => l.CreditNoteId == cn.Id).OrderBy(l => l.Id)
            .Select(l => new SourceLine(l.InvoiceLine.Description, l.Quantity, l.InvoiceLine.UnitPrice, l.Amount, l.Trip != null ? l.Trip.ReceiptNo : null,
                l.Trip != null ? l.Trip.LoadingSite.Name : null, l.Trip != null ? l.Trip.UnloadingSite.Name : null, l.Trip != null ? l.Trip.Material.Name : null))
            .ToListAsync(ct).ConfigureAwait(false);
        var built = Allocate(lines.Select(l => (Describe(l), l.Amount)).ToList(), cn.Subtotal, cn.DiscountShare, cn.FeesShare, inv.ExtraFeesDescription, cn.Tax, inv.TaxRate,
            company.CurrencyDecimals);
        return Document(EInvoiceCodes.CreditNote, cn.Number, cn.PublicId, cn.CreditDate, icv, inv.DueDate > inv.InvoiceDate, settings, company, buyer, built, cn.Total,
            new EInvoiceReference(inv.Number!, originalUuid, inv.Total), cn.Reason);
    }

    /// <summary>A receivable document of Accounting (D-15): its lines with their own rates; buyer = the accounting party.</summary>
    public async Task<EInvoiceDocument> ApArDocumentAsync(ApArDocument doc, long icv, EInvoiceSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(settings);
        var (company, buyer) = await ApArPartiesAsync(doc.PartyId, ct).ConfigureAwait(false);
        var lines = await db.Set<ApArDocumentLine>().AsNoTracking().Where(l => l.DocumentId == doc.Id).OrderBy(l => l.Id).Select(l => new { l.Description, l.Amount, l.TaxRate })
            .ToListAsync(ct).ConfigureAwait(false);
        var built = AllocateByRate(lines.Select(l => (l.Description, l.Amount, l.TaxRate)).ToList(), doc.Tax, company.CurrencyDecimals);
        return Document(EInvoiceCodes.Invoice, doc.Number, doc.PublicId, doc.DocumentDate, icv, doc.DueDate > doc.DocumentDate, settings, company, buyer, built, doc.Total, null, null);
    }

    /// <summary>A receivable return as a credit note: the returned amount split over the document's lines, their rates, the stored return tax.</summary>
    public async Task<EInvoiceDocument> ApArReturnAsync(ApArReturn ret, ApArDocument doc, Guid originalUuid, long icv, EInvoiceSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ret);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(settings);
        var (company, buyer) = await ApArPartiesAsync(doc.PartyId, ct).ConfigureAwait(false);
        var lines = await db.Set<ApArDocumentLine>().AsNoTracking().Where(l => l.DocumentId == doc.Id).OrderBy(l => l.Id).Select(l => new { l.Description, l.Amount, l.TaxRate })
            .ToListAsync(ct).ConfigureAwait(false);
        var shares = Split(ret.Amount, lines.Select(l => l.Amount).ToList(), company.CurrencyDecimals);
        var built = AllocateByRate(lines.Select((l, i) => (l.Description, shares[i], l.TaxRate)).ToList(), ret.Tax, company.CurrencyDecimals);
        return Document(EInvoiceCodes.CreditNote, ret.Number, ret.PublicId, ret.ReturnDate, icv, doc.DueDate > doc.DocumentDate, settings, company, buyer, built, ret.Total,
            new EInvoiceReference(doc.Number, originalUuid, doc.Total), ret.Reason);
    }

    private async Task<(Company Company, EInvoiceParty Buyer)> ApArPartiesAsync(long partyId, CancellationToken ct)
    {
        // By id: an archived or deleted party still appears on its documents.
        var party = await db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).SingleAsync(p => p.Id == partyId, ct).ConfigureAwait(false);
        var company = await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == party.CompanyId, ct).ConfigureAwait(false);
        return (company, new EInvoiceParty(party.Name, TaxId(party.TaxNumber), NullIfEmpty(TextNormalizer.DigitsOnly(party.Phone)), null));
    }

    private sealed record SourceLine(string? Description, decimal Quantity, decimal UnitPrice, decimal Amount, string? Receipt, string? Loading, string? Unloading, string? Material);

    /// <summary>Trip lines: receipt, route, material, quantity × price (the national document carries one amount per line).</summary>
    private static string Describe(SourceLine l)
    {
        if (l.Receipt is null) return l.Description ?? string.Empty;
        var qty = l.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
        var price = l.UnitPrice.ToString("0.000", CultureInfo.InvariantCulture);
        return $"نقل {l.Material} من {l.Loading} إلى {l.Unloading} - سند {l.Receipt} - {qty} × {price}";
    }

    private async Task<(Company Company, Customer Buyer)> PartiesAsync(long customerId, CancellationToken ct)
    {
        // By id: an archived or deleted customer still appears on its documents.
        var buyer = await db.Set<Customer>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).SingleAsync(c => c.Id == customerId, ct).ConfigureAwait(false);
        var company = await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == buyer.CompanyId, ct).ConfigureAwait(false);
        return (company, buyer);
    }

    private static EInvoiceDocument Document(string kind, string number, Guid uuid, DateOnly date, long icv, bool receivable, EInvoiceSettings settings, Company company,
        Customer buyer, IReadOnlyList<EInvoiceLine> lines, decimal total, EInvoiceReference? original, string? note) =>
        Document(kind, number, uuid, date, icv, receivable, settings, company,
            new EInvoiceParty(buyer.Name, TaxId(buyer.TaxNumber), NullIfEmpty(TextNormalizer.DigitsOnly(buyer.Phone)), buyer.City), lines, total, original, note);

    private static EInvoiceDocument Document(string kind, string number, Guid uuid, DateOnly date, long icv, bool receivable, EInvoiceSettings settings, Company company,
        EInvoiceParty buyer, IReadOnlyList<EInvoiceLine> lines, decimal total, EInvoiceReference? original, string? note)
    {
        var taxExclusive = lines.Sum(l => l.Quantity * l.UnitPrice);
        var discount = lines.Sum(l => l.Discount);
        var tax = lines.Sum(l => l.Tax);
        return new EInvoiceDocument(kind, number, uuid, date, icv, company.CurrencyCode, company.CurrencyDecimals, receivable,
            settings.RegistrationType ?? EInvoiceCodes.Sales, settings.IncomeSourceSequence,
            new EInvoiceParty(company.Name, settings.SellerTaxNumber, company.Phone, null),
            buyer,
            lines, taxExclusive, discount, tax, total, original, note);
    }

    /// <summary>Tax number with ASCII digits, without spaces and dashes; anything else is left for the format check.</summary>
    private static string? TaxId(string? value) =>
        NullIfEmpty(TextNormalizer.ToAsciiDigits(value).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal));

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Lines totalling exactly the stored amounts: net subtotal split over the source lines by their amounts, discount split over the
    /// lines by their net amounts, fees as a separate line, tax per line with the difference to the stored tax spread one unit per line.
    /// </summary>
    public static IReadOnlyList<EInvoiceLine> Allocate(IReadOnlyList<(string Description, decimal Weight)> source, decimal subtotal, decimal discount, decimal fees,
        string? feesDescription, decimal tax, decimal rate, int decimals)
    {
        ArgumentNullException.ThrowIfNull(source);
        var nets = Split(subtotal, source.Select(s => s.Weight).ToList(), decimals);
        var discounts = Split(discount, nets, decimals);
        var rows = source.Select((s, i) => (s.Description, Net: nets[i], Discount: discounts[i])).Where(r => r.Net != 0 || r.Discount != 0).ToList();
        if (fees != 0) rows.Add((string.IsNullOrWhiteSpace(feesDescription) ? "رسوم إضافية" : feesDescription!, fees, 0m));
        var taxes = rows.Select(r => Money.Round((r.Net - r.Discount) * rate / 100m, decimals)).ToArray();
        Spread(taxes, rows.Select(r => r.Net - r.Discount).ToList(), tax, decimals);
        return rows.Select((r, i) => new EInvoiceLine(i + 1, r.Description, 1m, r.Net, r.Discount, r.Net - r.Discount, rate, taxes[i])).ToList();
    }

    /// <summary>
    /// Lines with their own tax rates (AP/AR receivables, D-15): each line's tax rounded on its amount, the difference to the stored tax
    /// spread one smallest unit per taxable line, so the lines total exactly the stored amounts.
    /// </summary>
    public static IReadOnlyList<EInvoiceLine> AllocateByRate(IReadOnlyList<(string Description, decimal Net, decimal Rate)> source, decimal tax, int decimals)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rows = source.Where(r => r.Net != 0).ToList();
        var taxes = rows.Select(r => Money.Round(r.Net * r.Rate / 100m, decimals)).ToArray();
        Spread(taxes, rows.Select(r => r.Rate > 0 ? r.Net : 0m).ToList(), tax, decimals);
        return rows.Select((r, i) => new EInvoiceLine(i + 1, r.Description, 1m, r.Net, 0m, r.Net, r.Rate, taxes[i])).ToList();
    }

    /// <summary>Spreads the difference to the stored tax one smallest unit per line per round, largest taxable lines first, never below zero.</summary>
    private static void Spread(decimal[] taxes, List<decimal> taxable, decimal tax, int decimals)
    {
        var unit = Unit(decimals);
        var diff = tax - taxes.Sum();
        var order = Enumerable.Range(0, taxes.Length).Where(i => taxable[i] > 0).OrderByDescending(i => taxable[i]).ToList();
        var moved = true;
        while (diff != 0 && moved)
        {
            moved = false;
            foreach (var i in order)
            {
                if (diff == 0) break;
                var step = diff > 0 ? unit : -unit;
                if (taxes[i] + step < 0) continue;
                taxes[i] += step;
                diff -= step;
                moved = true;
            }
        }
    }

    private static decimal Unit(int decimals) => 1m / (decimal)Math.Pow(10, decimals);

    /// <summary>Splits an amount over weights in smallest units (largest remainder): the parts add up exactly to the amount.</summary>
    public static IReadOnlyList<decimal> Split(decimal amount, IReadOnlyList<decimal> weights, int decimals)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0) return [];
        var unit = Unit(decimals);
        var sum = weights.Sum();
        var exact = weights.Select(w => sum == 0 ? amount / weights.Count : amount * w / sum).ToList();
        var parts = exact.Select(e => Math.Floor(e / unit) * unit).ToArray();
        var left = (int)Math.Round((amount - parts.Sum()) / unit);
        foreach (var i in Enumerable.Range(0, parts.Length).OrderByDescending(i => exact[i] - parts[i]).ThenBy(i => i).Take(Math.Max(0, left)))
            parts[i] += unit;
        return parts;
    }

    /// <summary>
    /// Checks common to every market (FR-060): settings complete, seller and buyer names, lines present, totals consistent with the
    /// lines. Connectors add their own rules.
    /// </summary>
    public static IReadOnlyList<EInvoiceProblem> Check(EInvoiceDocument d, EInvoiceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(settings);
        var problems = new List<EInvoiceProblem>();
        if (string.IsNullOrWhiteSpace(settings.SellerTaxNumber) || !settings.SellerTaxNumber.All(char.IsAsciiDigit))
            problems.Add(new("sellerTaxNumber", ErrorCodes.EInvoiceSellerTaxNumber));
        if (string.IsNullOrWhiteSpace(settings.IncomeSourceSequence) || !settings.IncomeSourceSequence.All(char.IsAsciiDigit))
            problems.Add(new("incomeSourceSequence", ErrorCodes.EInvoiceIncomeSource));
        if (settings.ClientIdEncrypted is not { Length: > 0 } || settings.SecretKeyEncrypted is not { Length: > 0 })
            problems.Add(new("credentials", ErrorCodes.EInvoiceCredentials));
        if (string.IsNullOrWhiteSpace(d.Seller.Name)) problems.Add(new("seller.name", ErrorCodes.EInvoiceSellerName));
        if (string.IsNullOrWhiteSpace(d.Buyer.Name)) problems.Add(new("buyer.name", ErrorCodes.EInvoiceBuyerName));
        if (d.Buyer.TaxNumber is { } tin && !tin.All(char.IsAsciiDigit)) problems.Add(new("buyer.taxNumber", ErrorCodes.EInvoiceBuyerTaxNumber));
        if (d.Kind == EInvoiceCodes.CreditNote && string.IsNullOrWhiteSpace(d.Note)) problems.Add(new("reason", ErrorCodes.ValidationRequired));   // review L4
        if (d.Lines.Count == 0) problems.Add(new("lines", ErrorCodes.EInvoiceNoLines));
        else if (d.Lines.Any(l => l.NetAmount != l.Quantity * l.UnitPrice - l.Discount || l.Tax < 0 || l.Discount < 0)
                 || d.TaxExclusive - d.Discount + d.Tax != d.Payable)
            problems.Add(new("totals", ErrorCodes.EInvoiceTotals));
        return problems;
    }
}
