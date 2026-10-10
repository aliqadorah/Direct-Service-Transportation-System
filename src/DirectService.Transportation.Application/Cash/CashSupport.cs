using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>The money side of a cash document: method, cash box or bank account and the ledger account behind it.</summary>
public sealed record MoneyChoice(string Method, long? CashBoxId, long? BankAccountId, long AccountId, string? Reference);

/// <summary>
/// Rules shared by receipts, advances, settlements and partner payments (7.4.1, 7.4.5): payment method Cash / Bank / CliQ; cash → an
/// active cash box, bank transfer and CliQ → an active bank account; reference required for transfers and CliQ; dates not in the future
/// nor in a closed period (FR-089); the cash/bank posting line on the box's or bank's ledger account; voucher links and history.
/// </summary>
public sealed class CashSupport(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IUrlTokenService tokens)
{
    public bool En => ctx.Language == "en";

    /// <summary>The acting user (voids need one).</summary>
    public long UserId => ctx.UserId ?? throw new InvalidOperationException("This action needs a user.");

    /// <summary>The company's currency and its decimals.</summary>
    public async Task<(string Currency, int Decimals)> CurrencyAsync(CancellationToken ct)
    {
        var c = await db.Set<Domain.Core.Company>().AsNoTracking().Where(x => x.Id == ctx.CompanyId).Select(x => new { x.CurrencyCode, x.CurrencyDecimals }).SingleAsync(ct).ConfigureAwait(false);
        return (c.CurrencyCode, c.CurrencyDecimals);
    }

    /// <summary>The amount in words in the reader's language (BR-024).</summary>
    public async Task<string> WordsAsync(decimal amount, CancellationToken ct)
    {
        var (currency, decimals) = await CurrencyAsync(ct).ConfigureAwait(false);
        return En ? AmountInWords.English(amount, currency, decimals) : AmountInWords.Arabic(amount, currency, decimals);
    }

    /// <summary>A document date: required, not in the future (company time).</summary>
    public async Task<DateOnly> DateAsync(DateOnly? date, string field, CancellationToken ct)
    {
        if (date is not DateOnly d) throw Fail.Field(field, ErrorCodes.ValidationRequired, field);
        if (d > await support.TodayAsync(ct).ConfigureAwait(false)) throw Fail.Field(field, ErrorCodes.DateFuture, field);
        return d;
    }

    /// <summary>A party chosen on a cash document: any status (archived parties settle and pay their open items, DT-97).</summary>
    public async Task<T> PartyAsync<T>(string field, string? token, CancellationToken ct) where T : Domain.Common.AuditableEntity
    {
        var pid = support.Decode<T>(token) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, field);
        return await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == pid, ct).ConfigureAwait(false) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, field);
    }

    /// <summary>Method, cash box or bank account; the stored box/bank of an edited document stays allowed even if archived since.</summary>
    public async Task<MoneyChoice> ChooseAsync(string? method, string? cashBox, string? bankAccount, string? reference, CancellationToken ct,
        long? currentCashBoxId = null, long? currentBankAccountId = null)
    {
        var m = (method?.Trim().ToLowerInvariant()) switch
        {
            "cash" => CashCodes.Cash,
            "bank" or "transfer" => CashCodes.Bank,
            "cliq" => CashCodes.CliQ,
            null or "" => throw Fail.Field("paymentMethod", ErrorCodes.ValidationRequired, "paymentMethod"),
            _ => throw Fail.Field("paymentMethod", ErrorCodes.ValidationFormat, "paymentMethod"),
        };
        var refText = TextNormalizer.CleanText(TextNormalizer.ToAsciiDigits(reference));
        if (refText is { Length: > 100 }) throw Fail.Field("reference", ErrorCodes.ValidationRange, "reference");
        if (m == CashCodes.Cash)
        {
            if (!string.IsNullOrWhiteSpace(bankAccount)) throw Fail.Field("bankAccountId", ErrorCodes.ValidationFormat, "bankAccountId");
            if (string.IsNullOrWhiteSpace(cashBox)) throw Fail.Field("cashBoxId", ErrorCodes.ValidationRequired, "cashBoxId");
            var box = await support.ChooseAsync<CashBox>("cashBoxId", cashBox, null, b => b.Name, ct).ConfigureAwait(false);
            if (box.Status != AccountingCodes.Active && box.Id != currentCashBoxId) throw Fail.RuleOn("cashBoxId", ErrorCodes.RecordArchived, box.Name);
            return new MoneyChoice(m, box.Id, null, box.AccountId, refText);
        }
        if (!string.IsNullOrWhiteSpace(cashBox)) throw Fail.Field("cashBoxId", ErrorCodes.ValidationFormat, "cashBoxId");
        if (string.IsNullOrWhiteSpace(bankAccount)) throw Fail.Field("bankAccountId", ErrorCodes.ValidationRequired, "bankAccountId");
        if (refText is null) throw Fail.Field("reference", ErrorCodes.ValidationRequired, "reference");   // transfers and CliQ (7.4.1)
        var bank = await support.ChooseAsync<BankAccount>("bankAccountId", bankAccount, null, b => b.Name, ct).ConfigureAwait(false);
        if (bank.Status != AccountingCodes.Active && bank.Id != currentBankAccountId) throw Fail.RuleOn("bankAccountId", ErrorCodes.RecordArchived, bank.Name);
        return new MoneyChoice(m, null, bank.Id, bank.AccountId, refText);
    }

    /// <summary>The ledger account of a stored document's cash box or bank account.</summary>
    public async Task<long> AccountIdAsync(long? cashBoxId, long? bankAccountId, CancellationToken ct) => cashBoxId is long box
        ? await db.Set<CashBox>().IgnoreQueryFilters().Where(b => b.Id == box).Select(b => b.AccountId).SingleAsync(ct).ConfigureAwait(false)
        : await db.Set<BankAccount>().IgnoreQueryFilters().Where(b => b.Id == bankAccountId).Select(b => b.AccountId).SingleAsync(ct).ConfigureAwait(false);

    public static PostingLine Debit(long accountId, decimal amount) => new(null, accountId, amount, 0);

    public static PostingLine Credit(long accountId, decimal amount) => new(null, accountId, 0, amount);

    /// <summary>The cash box or bank account shown on a document.</summary>
    public MoneyAccountRef Account(Guid? cashBoxPid, string? cashBoxName, Guid? bankPid, string? bankName) => cashBoxPid is Guid c
        ? new MoneyAccountRef(CashCodes.CashBox, tokens.Encode<CashBox>(c), cashBoxName ?? string.Empty)
        : new MoneyAccountRef(CashCodes.BankAccount, tokens.Encode<BankAccount>(bankPid ?? Guid.Empty), bankName ?? string.Empty);

    public DocRef? Entry(Guid? pid, string? number) => pid is Guid g ? new DocRef(tokens.Encode<JournalEntry>(g), number) : null;

    /// <summary>A document that is not void, or the void refusal.</summary>
    public static void EnsureActive(ICashDocument d, string voidCode)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (d.Status == CashCodes.Void) throw Fail.Rule(voidCode, d.Number);
    }

    public static void RequireRowVersion(string? rowVersion)
    {
        if (string.IsNullOrEmpty(rowVersion)) throw Fail.Field("rowVersion", ErrorCodes.ValidationRequired, "rowVersion");
    }

    /// <summary>Amount > 0 in the currency's decimals.</summary>
    public static decimal Amount(decimal? amount, int decimals, string field = "amount")
    {
        if (amount is not decimal a || a <= 0 || decimal.Round(a, decimals) != a) throw Fail.Field(field, ErrorCodes.ValidationRange, field);
        return a;
    }

    /// <summary>Status filter of the cash lists: active (default), void or all.</summary>
    public static IQueryable<T> WithStatus<T>(IQueryable<T> q, string? status) where T : class, ICashDocument => (status?.Trim().ToLowerInvariant()) switch
    {
        null or "" or "active" => q.Where(d => d.Status == CashCodes.Active),
        "void" => q.Where(d => d.Status == CashCodes.Void),
        "all" => q,
        _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, "status"),
    };

    /// <summary>Method filter.</summary>
    public static string? Method(string? method) => (method?.Trim().ToLowerInvariant()) switch
    {
        null or "" => null,
        "cash" => CashCodes.Cash,
        "bank" => CashCodes.Bank,
        "cliq" => CashCodes.CliQ,
        _ => throw Fail.Field("method", ErrorCodes.ValidationFormat, "method"),
    };
}

/// <summary>Pending-posting retries and journal links shared by the cash documents (FR-101).</summary>
public abstract class CashPostingHandler<T>(IAppDbContext db, IUrlTokenService tokens) : IPostingSourceHandler where T : class, ICashDocument
{
    public virtual string SourceModule => AccountingCodes.ModuleTrn;

    public abstract string SourceType { get; }

    protected abstract string VoidCode { get; }

    protected abstract Task<PostingRequest?> EntryAsync(T document, CancellationToken ct);

    public async Task<PostingRequest?> BuildAsync(long sourceId, CancellationToken ct)
    {
        var d = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        return d is null || d.JournalEntryId is not null || d.Status == CashCodes.Void ? null : await EntryAsync(d, ct).ConfigureAwait(false);
    }

    public async Task AttachAsync(long sourceId, JournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var d = await db.Set<T>().SingleAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        // A retry racing a void or an edit must not leave an entry on a void document or a second entry.
        if (d.Status == CashCodes.Void) throw Fail.Rule(VoidCode, d.Number);
        if (d.JournalEntryId is not null) throw new AppException(ErrorCodes.ConcurrencyConflict, 409);
        d.JournalEntryId = entry.Id;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<long, SourceDocumentRef>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct) =>
        (await db.Set<T>().AsNoTracking().Where(d => sourceIds.Contains(d.Id)).Select(d => new { d.Id, d.PublicId, d.Number }).ToListAsync(ct).ConfigureAwait(false))
        .ToDictionary(d => d.Id, d => new SourceDocumentRef(SourceModule, SourceType, tokens.Encode<T>(d.PublicId), d.Number));
}
