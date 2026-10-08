using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Accounting periods (FR-132 … FR-134, BR-036, flow 9.12). One row per calendar month; a month without a row is Open.
/// Close/reopen need core.settings.manage and a reason and are audited. Closing takes the journal-number lock, so it
/// waits for postings in progress and no entry can slip into the month once it is closed.
/// </summary>
public sealed class PeriodService(IAppDbContext db, INumberingService numbering, IRequestContext ctx,
    IChangeContext changes, PendingPostingService pending, ChartOfAccountsService accounts, TimeProvider clock)
{
    /// <summary>The twelve months of a year with their status (default: the current year).</summary>
    public async Task<IReadOnlyList<PeriodResponse>> ListAsync(int? year, CancellationToken ct)
    {
        var y = year ?? clock.GetUtcNow().Year;
        if (y is < 2000 or > 2100) throw Fail.Field("year", ErrorCodes.ValidationRange, "year");
        var prefix = y.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-";
        var rows = await db.Set<Period>().AsNoTracking().Where(p => p.PeriodMonth.StartsWith(prefix))
            .Select(p => new { p.PeriodMonth, p.Status, p.ClosedAt, ClosedBy = p.ClosedByUser == null ? null : p.ClosedByUser.FullName, p.Reason, p.RowVersion })
            .ToDictionaryAsync(p => p.PeriodMonth, ct).ConfigureAwait(false);
        return Enumerable.Range(1, 12).Select(m =>
        {
            var month = $"{prefix}{m:00}";
            return rows.TryGetValue(month, out var p)
                ? new PeriodResponse(month, p.Status, p.ClosedAt, p.ClosedBy, p.Reason, Convert.ToBase64String(p.RowVersion))
                : new PeriodResponse(month, "Open", null, null, null, null);
        }).ToList();
    }

    /// <summary>FR-134: pending postings, unapproved drafts dated in the month, control accounts with lines lacking a party.</summary>
    public async Task<PreCloseCheckResponse> PreCloseCheckAsync(string month, CancellationToken ct)
    {
        EnsureMonth(month);
        var first = AccountingCodes.FirstDay(month);
        var last = first.AddMonths(1).AddDays(-1);
        var pendingCount = await pending.CountAsync(ct).ConfigureAwait(false);
        var drafts = await db.Set<Invoice>().AsNoTracking()
            .CountAsync(i => i.Status == "Draft" && i.InvoiceDate >= first && i.InvoiceDate <= last, ct).ConfigureAwait(false);
        // Sub-ledger check within the ledger: control-account amounts up to the month end that name no party
        // (e.g. account-level openings, DT-24). The full party reconciliation report is RPT-23 (FR-105, B9).
        var unreconciled = await db.Set<EntryLine>().AsNoTracking()
            .Where(l => l.Account.IsControlAccount && l.PartyId == null && l.JournalEntry.EntryDate <= last)
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Amount = g.Sum(l => l.Debit - l.Credit) })
            .Where(g => g.Amount != 0)
            .ToListAsync(ct).ConfigureAwait(false);
        var ids = unreconciled.Select(u => u.AccountId).ToList();
        var accountsById = await db.Set<Account>().AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);
        var list = unreconciled.Select(u => new UnreconciledAccount(accounts.Ref(accountsById[u.AccountId]), u.Amount))
            .OrderBy(u => u.Account.Code, StringComparer.Ordinal).ToList();
        return new PreCloseCheckResponse(month, pendingCount, drafts, list, pendingCount == 0 && drafts == 0 && list.Count == 0);
    }

    public async Task<PeriodResponse> CloseAsync(string month, PeriodActionRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        EnsureMonth(month);
        var reason = RequireReason(r.Reason);
        if (string.CompareOrdinal(month, AccountingCodes.Month(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))) > 0)
            throw Fail.Field("periodMonth", ErrorCodes.ValidationRange, "periodMonth");
        await db.InTransactionAsync(async c =>
        {
            await numbering.LockAsync(DocumentTypes.JournalEntry, c).ConfigureAwait(false);
            var period = await db.Set<Period>().SingleOrDefaultAsync(p => p.PeriodMonth == month, c).ConfigureAwait(false);
            if (period?.Status == "Closed") throw Fail.Rule(ErrorCodes.PeriodAlreadyClosed, month);
            var check = await PreCloseCheckAsync(month, c).ConfigureAwait(false);
            if (!check.Clean && !r.Confirm)
                throw Fail.Rule(ErrorCodes.PeriodChecksPending, check.PendingPostings, check.UnapprovedDrafts, check.UnreconciledControlAccounts.Count);
            if (period is null)
            {
                period = new Period { PeriodMonth = month };
                db.Set<Period>().Add(period);
            }
            period.Status = "Closed";
            period.ClosedBy = ctx.UserId;
            period.ClosedAt = clock.GetUtcNow().UtcDateTime;
            period.Reason = reason;
            changes.SetAction(period, "Close", reason);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await GetAsync(month, ct).ConfigureAwait(false);
    }

    /// <summary>Reopen (settings.manage, reason, audited). ClosedBy/ClosedAt record the reopening (DT-23).</summary>
    public async Task<PeriodResponse> ReopenAsync(string month, PeriodActionRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        EnsureMonth(month);
        var reason = RequireReason(r.Reason);
        var period = await db.Set<Period>().SingleOrDefaultAsync(p => p.PeriodMonth == month, ct).ConfigureAwait(false);
        if (period is null || period.Status != "Closed") throw Fail.Rule(ErrorCodes.PeriodNotClosed, month);
        period.Status = "Open";
        period.ClosedBy = ctx.UserId;
        period.ClosedAt = clock.GetUtcNow().UtcDateTime;
        period.Reason = reason;
        changes.SetAction(period, "Reopen", reason);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(month, ct).ConfigureAwait(false);
    }

    private async Task<PeriodResponse> GetAsync(string month, CancellationToken ct)
    {
        var year = int.Parse(month[..4], System.Globalization.CultureInfo.InvariantCulture);
        return (await ListAsync(year, ct).ConfigureAwait(false)).Single(p => p.PeriodMonth == month);
    }

    private static void EnsureMonth(string month)
    {
        if (!AccountingCodes.IsMonth(month)) throw Fail.Field("periodMonth", ErrorCodes.ValidationFormat, "periodMonth");
    }

    private static string RequireReason(string? reason)
    {
        var why = TextNormalizer.CleanText(reason) ?? throw Fail.Field("reason", ErrorCodes.ReasonRequired);
        return why.Length <= 500 ? why : why[..500];
    }
}
