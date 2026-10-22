using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// Driver and partner accounts and statements (FR-076, FR-083, FR-084, FR-088). Balance = what the company owes the driver / partner:
/// driver — wages of active own-fleet trips − active advances − deductions and net payments of active settlements; partner — partner
/// cost of active trips − active payments. Statements show the opening balance, each event with a running balance, totals and the
/// closing balance; partner statements carry the shipment details (date, receipt, route, weight, rate, cost).
/// </summary>
public sealed class AccountStatementService(IAppDbContext db, MasterDataSupport support, CashSupport cash, SettlementService settlements,
    PartnerPaymentService payments, ISpreadsheetService sheets)
{
    private const int KindTrip = 0, KindAdvance = 1, KindDeduction = 2, KindSettlement = 3, KindPayment = 4;

    private sealed record Event(DateOnly Date, int Order, long Id, string Kind, DocRef Document, string Description, decimal Increase, decimal Decrease,
        string? Route = null, decimal? Weight = null, decimal? Rate = null);

    // ============================================================ driver (FR-076, FR-083)

    public async Task<DriverAccountResponse> DriverAccountAsync(string token, CancellationToken ct)
    {
        var driver = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        var preview = await settlements.PreviewAsync(token, DateOnly.MaxValue, ct).ConfigureAwait(false);
        var events = await DriverEventsAsync(driver.Id, null, ct).ConfigureAwait(false);
        var history = await settlements.Rows(db.Set<Settlement>().AsNoTracking().Where(s => s.DriverId == driver.Id).OrderByDescending(s => s.SettlementDate).ThenByDescending(s => s.Id).Take(50))
            .ToListAsync(ct).ConfigureAwait(false);
        return new DriverAccountResponse(new Ref(support.Token<Driver>(driver.PublicId), driver.FullName), driver.PlateNumber, preview.Trips, preview.Advances, preview.Gross,
            preview.AdvancesTotal, preview.Net, events.Sum(e => e.Increase - e.Decrease), history.Select(settlements.ToResponse).ToList());
    }

    private async Task<List<Event>> DriverEventsAsync(long driverId, DateOnly? to, CancellationToken ct)
    {
        var upTo = to ?? DateOnly.MaxValue;
        var tripRows = await db.Set<Trip>().AsNoTracking().Where(t => t.DriverId == driverId && t.Status == TripService.Active && t.PartnerId == null && t.TripDate <= upTo)
            .Select(t => new { t.Id, t.PublicId, t.ReceiptNo, t.TripDate, Route = t.LoadingSite.Name + " ← " + t.UnloadingSite.Name, Material = t.Material.Name, t.WageQty, t.DriverRate, t.DriverWage })
            .ToListAsync(ct).ConfigureAwait(false);
        var advRows = await db.Set<Advance>().AsNoTracking().Where(a => a.DriverId == driverId && a.Status == CashCodes.Active && a.CarriedFromSettlementId == null && a.AdvanceDate <= upTo)
            .Select(a => new { a.Id, a.PublicId, a.Number, a.AdvanceDate, a.Amount }).ToListAsync(ct).ConfigureAwait(false);
        var stlRows = await db.Set<Settlement>().AsNoTracking().Where(s => s.DriverId == driverId && s.Status == CashCodes.Active && s.SettlementDate <= upTo)
            .Select(s => new { s.Id, s.PublicId, s.Number, s.SettlementDate, s.DeductionsTotal, s.NetPayable, s.RepaidAmount, s.PaymentMethod, s.Reference }).ToListAsync(ct).ConfigureAwait(false);
        var events = new List<Event>();
        events.AddRange(tripRows.Select(t => new Event(t.TripDate, KindTrip, t.Id, "Trip", new DocRef(support.Token<Trip>(t.PublicId), t.ReceiptNo),
            $"{t.Route} · {t.Material}", t.DriverWage, 0, t.Route, t.WageQty, t.DriverRate)));
        events.AddRange(advRows.Select(a => new Event(a.AdvanceDate, KindAdvance, a.Id, "Advance", new DocRef(support.Token<Advance>(a.PublicId), a.Number),
            cash.En ? "Advance" : "سلفة", 0, a.Amount)));
        foreach (var s in stlRows)
        {
            var doc = new DocRef(support.Token<Settlement>(s.PublicId), s.Number);
            if (s.DeductionsTotal > 0) events.Add(new Event(s.SettlementDate, KindDeduction, s.Id, "Deduction", doc, cash.En ? "Settlement deductions" : "خصومات التسوية", 0, s.DeductionsTotal));
            if (s.NetPayable > 0) events.Add(new Event(s.SettlementDate, KindSettlement, s.Id, "Settlement", doc, cash.En ? "Net paid" : "الصافي المدفوع", 0, s.NetPayable));
            if (s.RepaidAmount > 0)
                events.Add(new Event(s.SettlementDate, KindSettlement, s.Id, "Repaid", doc, CashSupport.RepaidText(cash.En, s.RepaidAmount, s.PaymentMethod, s.Reference), s.RepaidAmount, 0));
        }
        return events;
    }

    public async Task<StatementResponse> DriverStatementAsync(string token, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var driver = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        var (f, t) = await PeriodAsync(from, to, ct).ConfigureAwait(false);
        var events = await DriverEventsAsync(driver.Id, t, ct).ConfigureAwait(false);
        return Statement(new Ref(support.Token<Driver>(driver.PublicId), driver.FullName), f, t, events);
    }

    /// <summary>DT-230: the statement text of a trip voided or made cheaper after payment, by the choice made.</summary>
    private string OverpaidText(string handling, decimal amount, string? payment) => handling switch
    {
        CashCodes.Return => cash.En ? $"Voided / cheaper after payment: {Accounting.Money.Format(amount)} paid — the partner will return it" : $"أُلغيت أو خُفّضت بعد الدفع: {Accounting.Money.Format(amount)} مدفوع — سيعيده الشريك",
        CashCodes.Deduct => cash.En ? $"Voided / cheaper after payment: {Accounting.Money.Format(amount)} paid — deducted from the next payment(s)" : $"أُلغيت أو خُفّضت بعد الدفع: {Accounting.Money.Format(amount)} مدفوع — يُخصم من الدفعة التالية",
        _ => cash.En ? $"Payment {payment} was not actually made: voided ({Accounting.Money.Format(amount)})" : $"الدفعة {payment} لم تُصرف فعلاً: أُلغيت ({Accounting.Money.Format(amount)})",
    };

    // ============================================================ partner (FR-084, FR-088)

    public async Task<PartnerAccountResponse> PartnerAccountAsync(string token, CancellationToken ct)
    {
        var partner = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        var (cost, paid) = await payments.BalanceAsync(partner.Id, null, ct).ConfigureAwait(false);
        var count = await db.Set<Trip>().CountAsync(t => t.PartnerId == partner.Id && t.Status == TripService.Active, ct).ConfigureAwait(false);
        var recent = await payments.Rows(db.Set<PartnerPayment>().AsNoTracking().Where(p => p.PartnerId == partner.Id).OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id).Take(20))
            .ToListAsync(ct).ConfigureAwait(false);
        return new PartnerAccountResponse(new Ref(support.Token<Partner>(partner.PublicId), partner.Name), count, cost, paid, cost - paid, recent.Select(payments.ToResponse).ToList(),
            await payments.ToDeductAsync(partner.Id, ct).ConfigureAwait(false));
    }

    public async Task<StatementResponse> PartnerStatementAsync(string token, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var partner = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        var (f, t) = await PeriodAsync(from, to, ct).ConfigureAwait(false);
        var tripRows = await db.Set<Trip>().AsNoTracking().Where(x => x.PartnerId == partner.Id && x.Status == TripService.Active && x.TripDate <= t)
            .Select(x => new { x.Id, x.PublicId, x.ReceiptNo, x.TripDate, Route = x.LoadingSite.Name + " ← " + x.UnloadingSite.Name, Material = x.Material.Name, x.WageQty, x.PartnerRate, x.PartnerCost })
            .ToListAsync(ct).ConfigureAwait(false);
        var payRows = await db.Set<PartnerPayment>().AsNoTracking().Where(p => p.PartnerId == partner.Id && p.Status == CashCodes.Active && p.PaymentDate <= t)
            .Select(p => new { p.Id, p.PublicId, p.Number, p.PaymentDate, p.Amount, p.Kind, p.DeductedAmount }).ToListAsync(ct).ConfigureAwait(false);
        // DT-230: trips voided or made cheaper after payment, with the choice for the amount paid — information lines (no amount: the
        // balance already shows it through the payment that stays), and the deduction on the payment that set it off.
        var overRows = await db.Set<PartnerOverpayment>().AsNoTracking().Where(o => o.PartnerId == partner.Id)
            .Select(o => new { o.Id, o.CreatedAt, o.Amount, o.Handling, TripPid = o.Trip.PublicId, o.Trip.ReceiptNo, Payment = o.PartnerPayment != null ? o.PartnerPayment.Number : null })
            .ToListAsync(ct).ConfigureAwait(false);

        var events = tripRows.Select(x => new Event(x.TripDate, KindTrip, x.Id, "Trip", new DocRef(support.Token<Trip>(x.PublicId), x.ReceiptNo), $"{x.Route} · {x.Material}",
                x.PartnerCost, 0, x.Route, x.WageQty, x.PartnerRate))
            .Concat(payRows.Select(p => p.Kind == CashCodes.Repayment
                ? new Event(p.PaymentDate, KindPayment, p.Id, "Repayment", new DocRef(support.Token<PartnerPayment>(p.PublicId), p.Number),
                    cash.En ? "Repaid by the partner" : "سداد من الشريك", p.Amount, 0)
                : new Event(p.PaymentDate, KindPayment, p.Id, "Payment", new DocRef(support.Token<PartnerPayment>(p.PublicId), p.Number),
                    (cash.En ? "Payment" : "دفعة") + (p.DeductedAmount > 0
                        ? (cash.En ? $" · deducted {Accounting.Money.Format(p.DeductedAmount)} paid earlier for voided / changed trips" : $" · خُصم {Accounting.Money.Format(p.DeductedAmount)} مدفوع سابقاً عن رحلات ملغاة أو معدّلة")
                        : string.Empty), 0, p.Amount)))
            .ToList();
        events.AddRange(overRows.Select(o => new Event(DateOnly.FromDateTime(o.CreatedAt), KindPayment, o.Id,
            "Overpaid", new DocRef(support.Token<Trip>(o.TripPid), o.ReceiptNo), OverpaidText(o.Handling, o.Amount, o.Payment), 0, 0))
            .Where(e => e.Date <= t));
        return Statement(new Ref(support.Token<Partner>(partner.PublicId), partner.Name), f, t, events);
    }

    // ============================================================ common

    private async Task<(DateOnly From, DateOnly To)> PeriodAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var t = to ?? today;
        var f = from ?? new DateOnly(t.Year, t.Month, 1);
        if (f > t) throw Fail.Field("from", ErrorCodes.ValidationRange, new FieldRef("from"));
        return (f, t);
    }

    private static StatementResponse Statement(Ref party, DateOnly from, DateOnly to, List<Event> events)
    {
        var opening = events.Where(e => e.Date < from).Sum(e => e.Increase - e.Decrease);
        var running = opening;
        var lines = new List<StatementLine>();
        foreach (var e in events.Where(e => e.Date >= from && e.Date <= to).OrderBy(e => e.Date).ThenBy(e => e.Order).ThenBy(e => e.Id))
        {
            running += e.Increase - e.Decrease;
            lines.Add(new StatementLine(e.Date, e.Kind, e.Document, e.Description, e.Increase, e.Decrease, running, e.Route, e.Weight, e.Rate));
        }
        return new StatementResponse(party, from, to, opening, lines, lines.Sum(l => l.Increase), lines.Sum(l => l.Decrease), running);
    }

    /// <summary>A statement as Excel or CSV (FR-083, FR-088).</summary>
    public GeneratedFile Export(StatementResponse s, string name, string? format)
    {
        ArgumentNullException.ThrowIfNull(s);
        string[] headers = cash.En
            ? ["Date", "Type", "Document", "Description", "Weight", "Rate", "Increase", "Decrease", "Balance"]
            : ["التاريخ", "النوع", "المستند", "البيان", "الوزن", "السعر", "له", "عليه", "الرصيد"];
        var rows = new List<IReadOnlyList<object?>> { new object?[] { s.From, cash.En ? "Opening balance" : "الرصيد الافتتاحي", null, null, null, null, null, null, s.Opening } };
        rows.AddRange(s.Lines.Select(l => (IReadOnlyList<object?>)[l.Date, l.Kind, l.Document.Number, l.Description, l.Weight, l.Rate, l.Increase, l.Decrease, l.Balance]));
        rows.Add([s.To, cash.En ? "Closing balance" : "الرصيد الختامي", null, null, null, null, s.TotalIncrease, s.TotalDecrease, s.Closing]);
        return CashExport.File(name, format, headers, rows, sheets);
    }
}
