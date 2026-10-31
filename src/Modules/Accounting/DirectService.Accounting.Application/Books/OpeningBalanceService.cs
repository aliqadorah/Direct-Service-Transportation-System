using DirectService.Accounting.Contracts.Errors;
using DirectService.Accounting.Application.Posting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Accounting.Application.Books;

/// <summary>
/// Opening balances (acc.acct.setup; FR-110, 8.6 "Opening balances", G-04). Openings are kept on the accounts
/// (separate debit and credit) and posted as one opening entry (ACC/Opening). Posting again reverses the previous
/// opening entry and posts the new one. While setup is open an unbalanced opening is accepted with the difference on
/// the ACC.OpeningBalanceDifference account; after setup it is refused. The warning stays until that account's balance is 0.000.
/// </summary>
public sealed class OpeningBalanceService(IAppDbContext db, PostingEngine engine, IUrlTokenService tokens, IChangeContext changes)
{
    public async Task<OpeningBalancesResponse> GetAsync(CancellationToken ct)
    {
        var accounts = await db.Set<Account>().AsNoTracking()
            .Where(a => a.IsPostable && (a.Status == AccountingCodes.Active || a.OpeningDebit > 0 || a.OpeningCredit > 0))
            .OrderBy(a => a.Code).ToListAsync(ct).ConfigureAwait(false);
        var rows = accounts.Select(a => new OpeningBalanceRow(Ref(a), a.AccountType, a.OpeningDebit, a.OpeningCredit)).ToList();
        var totalDebit = accounts.Sum(a => a.OpeningDebit);
        var totalCredit = accounts.Sum(a => a.OpeningCredit);
        var posted = await CurrentOpeningEntryAsync(tracked: false, ct).ConfigureAwait(false);
        return new OpeningBalancesResponse(rows, totalDebit, totalCredit, totalDebit - totalCredit,
            await engine.IsSetupOpenAsync(ct).ConfigureAwait(false),
            posted is null ? null : new OpeningEntryInfo(tokens.Encode<JournalEntry>(posted.PublicId), posted.Number, posted.EntryDate, posted.TotalDebit),
            await DifferenceBalanceAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Sets the opening debit/credit of the listed postable accounts (others unchanged). Not posted until <see cref="PostAsync"/>.</summary>
    public async Task<OpeningBalancesResponse> UpdateAsync(OpeningBalancesRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var lines = r.Lines ?? [];
        var decimals = await engine.CurrencyDecimalsAsync(ct).ConfigureAwait(false);
        var ids = new Dictionary<Guid, OpeningBalanceLine>();
        var v = new Validator();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var id = tokens.Decode<Account>(l.AccountId);
            var field = $"lines[{i}]";
            v.Check(id is not null && !ids.ContainsKey(id.Value), field + ".accountId")
             .Check(Money.IsValidAmount(l.Debit, decimals) && Money.IsValidAmount(l.Credit, decimals) && !(l.Debit > 0 && l.Credit > 0),
                 field + ".debit", SharedErrorCodes.ValidationRange);
            if (id is not null) ids.TryAdd(id.Value, l);
        }
        v.ThrowIfInvalid();
        var keys = ids.Keys.ToList();
        var accounts = await db.Set<Account>().Where(a => keys.Contains(a.PublicId)).ToListAsync(ct).ConfigureAwait(false);
        if (accounts.Count != keys.Count) throw Fail.NotFound();
        foreach (var a in accounts)
        {
            var l = ids[a.PublicId];
            if (l.Debit == a.OpeningDebit && l.Credit == a.OpeningCredit) continue;
            if (!a.IsPostable || (a.Status != AccountingCodes.Active && (l.Debit > 0 || l.Credit > 0)))
                throw Fail.Rule(AccountingErrorCodes.AccountNotPostable, a.Code);
            a.OpeningDebit = l.Debit;
            a.OpeningCredit = l.Credit;
            changes.SetAction(a, "OpeningBalance");
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Posts the opening entry (replacing the previous one by reversal).</summary>
    public async Task<OpeningBalancesResponse> PostAsync(PostOpeningRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var previous = await CurrentOpeningEntryAsync(tracked: true, ct).ConfigureAwait(false);
        var date = r.EntryDate ?? previous?.EntryDate;
        if (date is null) throw Fail.Field("entryDate", SharedErrorCodes.ValidationRequired, new FieldRef("entryDate"));

        var accounts = await db.Set<Account>().AsNoTracking().Where(a => a.OpeningDebit > 0 || a.OpeningCredit > 0)
            .OrderBy(a => a.Code).ToListAsync(ct).ConfigureAwait(false);
        var lines = accounts.Select(a => new PostingLine(null, a.Id, a.OpeningDebit, a.OpeningCredit)).ToList();
        var difference = accounts.Sum(a => a.OpeningDebit) - accounts.Sum(a => a.OpeningCredit);
        if (difference != 0)
        {
            if (!await engine.IsSetupOpenAsync(ct).ConfigureAwait(false))
                throw Fail.Rule(AccountingErrorCodes.OpeningUnbalanced, Money.Format(difference)); // after setup: refused (FR-110)
            lines.Add(difference > 0
                ? PostingLine.CreditKey(AccountingPostingKeys.OpeningBalanceDifference, difference)
                : PostingLine.DebitKey(AccountingPostingKeys.OpeningBalanceDifference, -difference));
        }
        if (lines.Count == 0 && previous is null) throw Fail.Rule(AccountingErrorCodes.OpeningEmpty);

        await db.InTransactionAsync(async c =>
        {
            if (previous is not null)
                await engine.ReverseAsync(previous, $"عكس القيد الافتتاحي {previous.Number} / Reversal of opening entry {previous.Number}", null, c).ConfigureAwait(false);
            if (lines.Count > 0)
            {
                var request = new PostingRequest(date.Value, "القيد الافتتاحي / Opening balances", AccountingCodes.ModuleAcc,
                    AccountingCodes.SourceOpening, null, false, lines);
                await engine.PostAsync(request, c).ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
        return await GetAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Balance (debit − credit) of the opening-difference account; the warning shows while it is not 0.000 (FR-110).</summary>
    public async Task<decimal> DifferenceBalanceAsync(CancellationToken ct)
    {
        var accountId = await db.Set<PostingKeyMapping>().AsNoTracking().Where(m => m.PostingKey == AccountingPostingKeys.OpeningBalanceDifference)
            .Select(m => (long?)m.AccountId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (accountId is null) return 0;
        return await db.Set<EntryLine>().AsNoTracking().Where(l => l.AccountId == accountId)
            .SumAsync(l => l.Debit - l.Credit, ct).ConfigureAwait(false);
    }

    private Task<JournalEntry?> CurrentOpeningEntryAsync(bool tracked, CancellationToken ct)
    {
        var q = tracked ? db.Set<JournalEntry>() : db.Set<JournalEntry>().AsNoTracking();
        return q.Where(e => e.SourceModule == AccountingCodes.ModuleAcc && e.SourceType == AccountingCodes.SourceOpening
                            && e.Status == AccountingCodes.Posted && e.ReversesEntryId == null)
            .OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct);
    }

    private AccountRef Ref(Account a) => new(tokens.Encode<Account>(a.PublicId), a.Code, a.NameAr, a.NameEn);
}
