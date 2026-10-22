using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Invoicing;

/// <summary>
/// Print layouts per company (FR-054 … FR-056): one per document type (claim statement, tax invoice); title, header band, grouping of
/// the claim's first page (loading site, route, material), annex columns in order (built-in trip columns and the trip custom fields),
/// labels, footer, signature boxes, logo and QR. A company without a saved layout prints the built-in default.
/// </summary>
public sealed class InvoiceLayoutService(IAppDbContext db, IRequestContext ctx, CustomFieldService customFields)
{
    public static readonly IReadOnlyList<string> BuiltInColumns =
        ["receiptNo", "truckPlate", "loadingDate", "unloadingDate", "loadedWeight", "unloadedWeight", "billingQty", "unitPrice", "amount", "driver", "loadingSite", "unloadingSite", "material"];

    private static readonly string[] Groupings = ["LoadingSite", "Route", "Material"];

    private bool En => ctx.Language == "en";

    /// <summary>
    /// The stored layout or the built-in default (RowVersion null) of a document type; with <paramref name="builtIn"/> the default content
    /// carrying the stored row version, so "restore default" can be saved over the stored layout (DT-154).
    /// </summary>
    public async Task<InvoiceLayoutResponse> GetAsync(string layoutType, CancellationToken ct, bool builtIn = false)
    {
        var type = Type(layoutType);
        var row = await db.Set<InvoiceLayout>().AsNoTracking().SingleOrDefaultAsync(l => l.LayoutType == type, ct).ConfigureAwait(false);
        var available = await AvailableAsync(ct).ConfigureAwait(false);
        if (row is null) return Default(type, available);
        return builtIn ? Default(type, available) with { RowVersion = Convert.ToBase64String(row.RowVersion) } : ToResponse(row, available);
    }

    /// <summary>The layout a request describes, validated like a save but not stored — the designer's live preview (FR-055, DT-154).</summary>
    public async Task<InvoiceLayoutResponse> DraftAsync(string layoutType, InvoiceLayoutRequest r, CancellationToken ct)
    {
        var type = Type(layoutType);
        var available = await AvailableAsync(ct).ConfigureAwait(false);
        var v = Validate(r, available);
        return new InvoiceLayoutResponse(type, v.Title, v.Band, r.Grouping ?? "LoadingSite", v.Columns, v.Labels, v.Footer, v.Signatures, r.ShowLogo, r.ShowQr, available, null);
    }

    public async Task<InvoiceLayoutResponse> SaveAsync(string layoutType, InvoiceLayoutRequest r, CancellationToken ct)
    {
        var type = Type(layoutType);
        var available = await AvailableAsync(ct).ConfigureAwait(false);
        var (title, band, footer, columns, signatures, labels) = Validate(r, available);
        var row = await db.Set<InvoiceLayout>().SingleOrDefaultAsync(l => l.LayoutType == type, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new InvoiceLayout { LayoutType = type };
            db.Set<InvoiceLayout>().Add(row);
        }
        else
        {
            // Saving over a stored layout needs the version last seen (review L8).
            if (string.IsNullOrEmpty(r.RowVersion)) throw Fail.Field("rowVersion", ErrorCodes.ValidationRequired, new FieldRef("rowVersion"));
            db.ExpectRowVersion(row, r.RowVersion);
        }
        row.Title = title;
        row.BandText = band;
        row.Grouping = r.Grouping ?? "LoadingSite";
        row.AnnexColumns = JsonSerializer.Serialize(columns);
        row.Labels = labels.Count == 0 ? null : JsonSerializer.Serialize(labels);
        row.FooterText = footer;
        row.Signatures = signatures.Count == 0 ? null : JsonSerializer.Serialize(signatures);
        row.ShowLogo = r.ShowLogo;
        row.ShowQr = r.ShowQr;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(row, available);
    }

    private static (string Title, string? Band, string? Footer, List<string> Columns, List<string> Signatures, Dictionary<string, string> Labels) Validate(
        InvoiceLayoutRequest r, IReadOnlyList<string> available)
    {
        ArgumentNullException.ThrowIfNull(r);
        var title = TextNormalizer.CleanText(r.Title);
        var band = TextNormalizer.CleanText(r.BandText);
        var footer = TextNormalizer.CleanText(r.FooterText);
        var columns = (r.AnnexColumns ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var signatures = (r.Signatures ?? []).Select(TextNormalizer.CleanText).Where(s => s is not null).Select(s => s!).ToList();
        // Keys equal after trimming keep the last label (no 500 on "amount" and "amount "), and only known columns are kept (review L5).
        var labels = (r.Labels ?? new Dictionary<string, string>()).Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && available.Contains(kv.Key.Trim(), StringComparer.Ordinal))
            .GroupBy(kv => kv.Key.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => TextNormalizer.CleanText(g.Last().Value)!, StringComparer.Ordinal);
        new Validator()
            .Require("title", title).MaxLength("title", title, 200).MaxLength("bandText", band, 300).MaxLength("footerText", footer, 500)
            .Check(r.Grouping is null || Groupings.Contains(r.Grouping, StringComparer.Ordinal), "grouping")
            .Check(columns.Count is > 0 and <= 30 && columns.All(c => available.Contains(c, StringComparer.Ordinal)), "annexColumns")
            .Check(columns.Contains("amount", StringComparer.Ordinal), "annexColumns", ErrorCodes.ValidationRequired)
            .Check(signatures.Count <= 6 && signatures.All(s => s.Length <= 60), "signatures", ErrorCodes.ValidationRange)
            .Check(labels.Count <= 60 && labels.All(kv => kv.Key.Length <= 60 && kv.Value.Length <= 100), "labels", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        return (title!, band, footer, columns, signatures, labels);
    }

    private async Task<IReadOnlyList<string>> AvailableAsync(CancellationToken ct) =>
        [.. BuiltInColumns, .. (await customFields.DefinitionsAsync(FieldCatalog.Trip, ct).ConfigureAwait(false)).Where(d => !d.IsHidden).Select(d => "custom." + d.FieldKey)];

    private static string Type(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "claim" => InvoiceCodes.Claim,
        "taxinvoice" or "tax-invoice" => InvoiceCodes.TaxInvoice,
        _ => throw Fail.Field("layoutType", ErrorCodes.ValidationFormat, new FieldRef("layoutType")),   // review L3
    };

    private InvoiceLayoutResponse Default(string type, IReadOnlyList<string> available) => new(type,
        type == InvoiceCodes.Claim ? (En ? "Claim statement" : "كشف مطالبة") : (En ? "Tax invoice" : "فاتورة ضريبية"), null, "LoadingSite",
        ["receiptNo", "truckPlate", "loadingDate", "unloadedWeight", "billingQty", "unitPrice", "amount"], new Dictionary<string, string>(), null,
        En ? ["Accountant", "Finance manager", "Received by"] : ["المحاسب", "المدير المالي", "المستلم"], true, true, available, null);

    /// <summary>Column keys written by companies provisioned before B6 ("ReceiptNo", "BilledWeight", "Price") in today's keys.</summary>
    private static string LegacyKey(string key) => key switch
    {
        "BilledWeight" => "billingQty",
        "Price" => "unitPrice",
        _ when key.Length > 0 && char.IsUpper(key[0]) && !key.StartsWith("custom.", StringComparison.Ordinal) => char.ToLowerInvariant(key[0]) + key[1..],
        _ => key,
    };

    internal static InvoiceLayoutResponse ToResponse(InvoiceLayout row, IReadOnlyList<string> available) => new(row.LayoutType, row.Title, row.BandText, row.Grouping,
        (JsonSerializer.Deserialize<List<string>>(row.AnnexColumns) ?? []).Select(LegacyKey).Distinct(StringComparer.Ordinal).ToList(),
        row.Labels is null ? new Dictionary<string, string>() : JsonSerializer.Deserialize<Dictionary<string, string>>(row.Labels) ?? [],
        row.FooterText, row.Signatures is null ? [] : JsonSerializer.Deserialize<List<string>>(row.Signatures) ?? [], row.ShowLogo, row.ShowQr, available,
        Convert.ToBase64String(row.RowVersion));
}
