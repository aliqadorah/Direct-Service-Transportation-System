using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// Driver and partner accounts and statements (FR-076, FR-083, FR-084, FR-088). Balance = what the company owes the driver / partner:
/// driver — wages of active own-fleet trips − active advances − deductions and net payments of active settlements; partner — partner
/// cost of active trips − active payments. Statements show the opening balance, each event with a running balance, totals and the
/// closing balance; partner statements carry the shipment details (date, receipt, route, weight, rate, cost).
/// Owner finding 27 Oct 2026 (DT-269): manual journal entries and the opening entry with the driver or partner as party (e.g. an opening
/// driver advance on 1104, which RPT-23 shows) are part of the account too: credit = owed to the party, debit = owed by it. RPT-19 / RPT-20
/// take every balance from <see cref="DriverBalancesAsync"/> / <see cref="PartnerBalancesAsync"/>, built from the same rules, so a balance
/// always equals the statement's closing balance on that date.
/// </summary>
public sealed class AccountStatementService(IAppDbContext db, MasterDataSupport support, CashSupport cash, SettlementService settlements,
    PartnerPaymentService payments, ISpreadsheetService sheets, PreviousBalances previous)
{
    private const int KindTrip = 0, KindAdvance = 1, KindDeduction = 2, KindSettlement = 3, KindPayment = 4, KindJournal = 5;

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
            preview.AdvancesTotal, preview.Net, events.Sum(e => e.Increase - e.Decrease), history.Select(settlements.ToResponse).ToList(), preview.PreviousBalance,
            preview.PreviousBalanceEntries);
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
        events.AddRange(await JournalEventsAsync(AccountingCodes.PartyDriver, driverId, upTo, ct).ConfigureAwait(false));
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
        CashCodes.Return => cash.En ? $"Voided / cheaper after payment: {Money.Format(amount)} paid — the partner will return it" : $"أُلغيت أو خُفّضت بعد الدفع: {Money.Format(amount)} مدفوع — سيعيده الشريك",
        CashCodes.Deduct => cash.En ? $"Voided / cheaper after payment: {Money.Format(amount)} paid — deducted from the next payment(s)" : $"أُلغيت أو خُفّضت بعد الدفع: {Money.Format(amount)} مدفوع — يُخصم من الدفعة التالية",
        _ => cash.En ? $"Payment {payment} was not actually made: voided ({Money.Format(amount)})" : $"الدفعة {payment} لم تُصرف فعلاً: أُلغيت ({Money.Format(amount)})",
    };

    // ============================================================ partner (FR-084, FR-088)

    public async Task<PartnerAccountResponse> PartnerAccountAsync(string token, CancellationToken ct)
    {
        var partner = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        var (cost, paid) = await payments.BalanceAsync(partner.Id, null, ct).ConfigureAwait(false);
        var count = await db.Set<Trip>().CountAsync(t => t.PartnerId == partner.Id && t.Status == TripService.Active, ct).ConfigureAwait(false);
        var recent = await payments.Rows(db.Set<PartnerPayment>().AsNoTracking().Where(p => p.PartnerId == partner.Id).OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id).Take(20))
            .ToListAsync(ct).ConfigureAwait(false);
        // DT-269 / DT-277: the balance includes manual / opening entries with the partner as party, like the statement: "paid" is what payments paid
        // for the trips, the previous balance what is still open of those entries (the rest was settled with payments).
        var open = await previous.OpenAsync(AccountingCodes.PartyPartner, partner.Id, DateOnly.MaxValue, ct).ConfigureAwait(false);
        var balance = open.Sum(o => o.Amount);
        return new PartnerAccountResponse(new Ref(support.Token<Partner>(partner.PublicId), partner.Name), count, cost, paid, cost - paid + balance, recent.Select(payments.ToResponse).ToList(),
            await payments.ToDeductAsync(partner.Id, ct).ConfigureAwait(false), balance, balance == 0 ? [] : await previous.EntriesAsync(open, ct).ConfigureAwait(false));
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
                        ? (cash.En ? $" · deducted {Money.Format(p.DeductedAmount)} paid earlier for voided / changed trips" : $" · خُصم {Money.Format(p.DeductedAmount)} مدفوع سابقاً عن رحلات ملغاة أو معدّلة")
                        : string.Empty), 0, p.Amount)))
            .ToList();
        events.AddRange(await JournalEventsAsync(AccountingCodes.PartyPartner, partner.Id, t, ct).ConfigureAwait(false));
        events.AddRange(overRows.Select(o => new Event(DateOnly.FromDateTime(o.CreatedAt), KindPayment, o.Id,
            "Overpaid", new DocRef(support.Token<Trip>(o.TripPid), o.ReceiptNo), OverpaidText(o.Handling, o.Amount, o.Payment), 0, 0))
            .Where(e => e.Date <= t));
        return Statement(new Ref(support.Token<Partner>(partner.PublicId), partner.Name), f, t, events);
    }

    // ============================================================ manual and opening entries with the party (DT-269)

    /// <summary>Lines of manual entries (with their reversals) and of the opening entry that name the party, up to a date.</summary>
    private IQueryable<EntryLine> PartyJournalLines(string partyType, DateOnly upTo) => PreviousBalances.PartyLines(db, partyType, upTo);

    private async Task<List<Event>> JournalEventsAsync(string partyType, long partyId, DateOnly upTo, CancellationToken ct)
    {
        var rows = await PartyJournalLines(partyType, upTo).Where(l => l.PartyId == partyId)
            .Select(l => new { l.Id, l.JournalEntry.EntryDate, l.JournalEntry.PublicId, l.JournalEntry.Number, l.JournalEntry.Description, l.Note, l.Debit, l.Credit })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new Event(r.EntryDate, KindJournal, r.Id, "Journal", new DocRef(support.Token<JournalEntry>(r.PublicId), r.Number),
            string.IsNullOrWhiteSpace(r.Note) ? r.Description : $"{r.Description} · {r.Note}", r.Credit, r.Debit)).ToList();
    }

    private async Task<Dictionary<long, decimal>> JournalBalancesAsync(string partyType, DateOnly upTo, CancellationToken ct) =>
        await PartyJournalLines(partyType, upTo).GroupBy(l => l.PartyId!.Value).Select(g => new { g.Key, B = g.Sum(l => l.Credit - l.Debit) })
            .ToDictionaryAsync(x => x.Key, x => x.B, ct).ConfigureAwait(false);

    // ============================================================ balances of every driver / partner (RPT-19, RPT-20; DT-269)

    /// <summary>A driver's account on a date, by part: wages − advances − settled (deductions and net paid, less repaid) + journal entries.</summary>
    public sealed record DriverBalance(long DriverId, int Trips, decimal Wages, decimal Advances, decimal Settled, decimal Journal)
    {
        public decimal Balance => Wages - Advances - Settled + Journal;
    }

    /// <summary>A partner's account on a date, by part: trip cost − payments + repayments + journal entries.</summary>
    public sealed record PartnerBalance(long PartnerId, int Trips, decimal Cost, decimal Payments, decimal Repayments, decimal Journal)
    {
        public decimal Balance => Cost - Payments + Repayments + Journal;
    }

    /// <summary>Every driver with something on the account up to <paramref name="asOf"/>, by the rules of the driver statement.</summary>
    public async Task<List<DriverBalance>> DriverBalancesAsync(DateOnly asOf, CancellationToken ct)
    {
        var wages = await db.Set<Trip>().AsNoTracking().Where(t => t.Status == TripService.Active && t.PartnerId == null && t.TripDate <= asOf)
            .GroupBy(t => t.DriverId).Select(g => new { g.Key, N = g.Count(), A = g.Sum(t => t.DriverWage) }).ToDictionaryAsync(x => x.Key, ct).ConfigureAwait(false);
        var advances = await db.Set<Advance>().AsNoTracking().Where(a => a.Status == CashCodes.Active && a.CarriedFromSettlementId == null && a.AdvanceDate <= asOf)
            .GroupBy(a => a.DriverId).Select(g => new { g.Key, A = g.Sum(a => a.Amount) }).ToDictionaryAsync(x => x.Key, x => x.A, ct).ConfigureAwait(false);
        var settled = await db.Set<Settlement>().AsNoTracking().Where(s => s.Status == CashCodes.Active && s.SettlementDate <= asOf)
            .GroupBy(s => s.DriverId).Select(g => new
            {
                g.Key,
                A = g.Sum(s => (s.DeductionsTotal > 0 ? s.DeductionsTotal : 0) + (s.NetPayable > 0 ? s.NetPayable : 0) - (s.RepaidAmount > 0 ? s.RepaidAmount : 0)),
            })
            .ToDictionaryAsync(x => x.Key, x => x.A, ct).ConfigureAwait(false);
        var journal = await JournalBalancesAsync(AccountingCodes.PartyDriver, asOf, ct).ConfigureAwait(false);
        return wages.Keys.Union(advances.Keys).Union(settled.Keys).Union(journal.Keys).Distinct()
            .Select(id => new DriverBalance(id, wages.TryGetValue(id, out var w) ? w.N : 0, w?.A ?? 0, advances.GetValueOrDefault(id), settled.GetValueOrDefault(id),
                journal.GetValueOrDefault(id)))
            .ToList();
    }

    /// <summary>Every partner with something on the account up to <paramref name="asOf"/>, by the rules of the partner statement.</summary>
    public async Task<List<PartnerBalance>> PartnerBalancesAsync(DateOnly asOf, CancellationToken ct)
    {
        var cost = await db.Set<Trip>().AsNoTracking().Where(t => t.Status == TripService.Active && t.PartnerId != null && t.TripDate <= asOf)
            .GroupBy(t => t.PartnerId!.Value).Select(g => new { g.Key, N = g.Count(), A = g.Sum(t => t.PartnerCost) }).ToDictionaryAsync(x => x.Key, ct).ConfigureAwait(false);
        var pays = await db.Set<PartnerPayment>().AsNoTracking().Where(p => p.Status == CashCodes.Active && p.PaymentDate <= asOf)
            .GroupBy(p => p.PartnerId).Select(g => new
            {
                g.Key,
                Paid = g.Sum(p => p.Kind == CashCodes.Repayment ? 0 : p.Amount),
                Repaid = g.Sum(p => p.Kind == CashCodes.Repayment ? p.Amount : 0),
            })
            .ToDictionaryAsync(x => x.Key, ct).ConfigureAwait(false);
        var journal = await JournalBalancesAsync(AccountingCodes.PartyPartner, asOf, ct).ConfigureAwait(false);
        return cost.Keys.Union(pays.Keys).Union(journal.Keys).Distinct()
            .Select(id => new PartnerBalance(id, cost.TryGetValue(id, out var c) ? c.N : 0, c?.A ?? 0, pays.TryGetValue(id, out var p) ? p.Paid : 0, p?.Repaid ?? 0,
                journal.GetValueOrDefault(id)))
            .ToList();
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
