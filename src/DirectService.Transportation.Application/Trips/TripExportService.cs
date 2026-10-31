using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;

namespace DirectService.Transportation.Application.Trips;

/// <summary>
/// Excel / CSV export of the filtered trip list (FR-009, FR-017, FR-018 data): the company's field labels, the visible custom
/// fields, frozen prices, amounts, flags and status; or the printout (PDF / HTML, FR-150).
/// </summary>
public sealed class TripExportService(TripService trips, ISpreadsheetService sheets, IRequestContext ctx, IStandardFields fields, ICustomFields customFields,
    IReportExporter exporter)
{
    public async Task<GeneratedFile> ExportAsync(TripListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var csv = (format?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "xlsx" or "pdf" or "html" => false,
            "csv" => true,
            _ => throw Fail.Field("format", ErrorCodes.ValidationFormat, new FieldRef("format")),
        };
        var en = ctx.Language == "en";
        var rules = await fields.RulesAsync(FieldCatalog.Trip, ct).ConfigureAwait(false);
        string L(string key) => rules.Label(key);
        string X(string ar, string e) => en ? e : ar;
        var defs = (await customFields.DefinitionsAsync(FieldCatalog.Trip, ct).ConfigureAwait(false)).Where(d => !d.IsHidden).ToList();
        var headers = new List<string>
        {
            L("receiptNo"), L("tripDate"), L("loadingDate"), L("unloadingDate"), L("driverId"), X("اللوحة", "Plate"), L("customerId"), L("loadingSiteId"),
            L("unloadingSiteId"), L("materialId"), L("partnerId"), X("نوع الأسطول", "Fleet type"), L("loadedWeight"), L("unloadedWeight"), X("كمية الفوترة", "Billing qty"),
            L("customerPrice"), X("سعر التعرفة", "Tariff price"), L("driverRate"), X("مصدر الأجرة", "Rate source"), X("شهر الأجرة", "Rate month"), L("partnerRate"),
            X("الإيراد", "Revenue"), X("أجر السائق", "Driver wage"), X("تكلفة الشريك", "Partner cost"), X("الهامش", "Margin"), X("النقص ٪", "Shortage %"),
            X("نقص", "Shortage flag"), X("مخالفة تعرفة", "Tariff breach"), X("تحتاج مراجعة", "Needs review"), X("الحالة", "Status"), X("الفوترة", "Billing"),
            X("رقم الفاتورة", "Invoice no."), X("التسوية / الدفع", "Settlement / payment"), X("المتبقي للشريك", "Open to partner"), L("notes"),
        };
        headers.AddRange(defs.Select(customFields.Label));
        var rows = new List<IReadOnlyList<object?>>();
        var (all, cuts) = await trips.AllAsync(q, ct).ConfigureAwait(false);
        var open = await trips.PartnerOpenAsync(all, ct, cuts).ConfigureAwait(false);
        foreach (var r in all)
        {
            var t = trips.ToResponse(r, open);
            var values = CustomFieldValues.Read(r.T.CustomValues);
            rows.Add([.. new object?[]
            {
                t.ReceiptNo, t.TripDate, t.LoadingDate, t.UnloadingDate, t.Driver.Name, t.TruckPlate, t.Customer.Name, t.LoadingSite.Name, t.UnloadingSite.Name,
                t.Material.Name, t.Partner?.Name, FleetType(t, en), t.LoadedWeight, t.UnloadedWeight, t.BillingQty, t.CustomerPrice, t.TariffPrice, t.DriverRate,
                t.RateSource, t.RateMonth, t.PartnerRate, t.Revenue, t.DriverWage, t.PartnerCost, t.Margin, t.ShortagePct, t.IsShortageFlagged, t.IsTariffBreach,
                t.NeedsReview, t.Status, t.Billing, t.Invoice?.Number, PaymentText(t.Payment, en), t.PartnerOpen, t.Notes,
            }, .. defs.Select(d => values.TryGetValue(d.FieldKey, out var v) ? Cell(v) : null)]);
        }
        if (IReportExporter.IsPrintout(format))   // FR-150: the printout keeps the columns that fit A4 (receipt, date, parties, route, weights, amounts, status)
            return await exporter.ListPrintoutAsync(X("الرحلات", "Trips"), "trips", headers, rows, format!, ct, [0, 1, 4, 6, 10, 7, 8, 9, 12, 13, 15, 21, 22, 29, 31, 32],
                totals: true, filters: q.From is null && q.To is null ? null : [(X("الفترة", "Period"), $"{q.From:yyyy-MM-dd} – {q.To:yyyy-MM-dd}")],
                quantities: [12, 13, 14, 25], sums: [12, 13, 21, 22]).ConfigureAwait(false);   // weights print as numbers; the unit price is not totalled   // also the trip statement of a driver, customer or partner (FR-018)
        return csv
            ? new GeneratedFile("trips.csv", "text/csv", sheets.WriteCsv(headers, rows))
            : new GeneratedFile("trips.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sheets.WriteXlsx("trips", headers, rows));
    }

    /// <summary>DT-273: own fleet / external / partner (a partner trip whatever the driver's ownership type).</summary>
    internal static string FleetType(TripResponse t, bool en) =>
        t.Partner is not null ? (en ? "Partner" : "شريك") : t.FleetClass == "Own" ? (en ? "Own fleet" : "أسطول الشركة") : (en ? "External" : "خارجي");

    /// <summary>DT-271 / DT-272: the trip list's status words for settlement and partner payment.</summary>
    internal static string? PaymentText(string? payment, bool en) => payment switch
    {
        "Settled" => en ? "Settled" : "مسوّاة",
        "Unsettled" => en ? "Not settled" : "غير مسوّاة",
        "PartnerPaid" => en ? "Paid to partner" : "مدفوعة للشريك",
        "PartnerPartlyPaid" => en ? "Partly paid" : "مدفوعة جزئياً",
        "PartnerUnpaid" => en ? "Not paid to partner" : "غير مدفوعة للشريك",
        _ => null,
    };

    private static object? Cell(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number when v.TryGetDecimal(out var n) => n,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => CustomFieldValues.Text(v),
    };
}
