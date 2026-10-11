using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Cost centres (FR-131) and cash boxes / bank accounts (FR-129), acc.acct.setup. Each cash box and bank account has
/// its own postable asset account ("one account per cash box / bank account", 7.5.1). Archived records stay in
/// history but cannot be chosen on new documents; delete (soft) only while unreferenced (BR-040).
/// </summary>
public sealed partial class CashBankService(IAppDbContext db, IUrlTokenService tokens, IChangeContext changes, ChartOfAccountsService accounts, IRequestContext ctx,
    MasterData.MasterDataSupport support)
{
    // ============================================================ cost centres

    /// <summary>Active cost centres for the pick lists of journal and AP/AR lines (FR-131).</summary>
    public async Task<IReadOnlyList<MasterOption>> CostCentreOptionsAsync(CancellationToken ct) =>
        (await db.Set<CostCentre>().AsNoTracking().Where(c => c.Status == AccountingCodes.Active).OrderBy(c => c.Code).Select(c => new { c.PublicId, c.Code, c.Name })
            .ToListAsync(ct).ConfigureAwait(false)).Select(c => new MasterOption(tokens.Encode<CostCentre>(c.PublicId), c.Code + " · " + c.Name)).ToList();

    public async Task<IReadOnlyList<CostCentreResponse>> CostCentresAsync(CancellationToken ct) =>
        (await db.Set<CostCentre>().AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct).ConfigureAwait(false)).Select(ToResponse).ToList();

    public async Task<CostCentreResponse> CreateCostCentreAsync(CostCentreRequest r, CancellationToken ct)
    {
        var (code, name) = ValidateCostCentre(r);
        if (await db.Set<CostCentre>().AnyAsync(c => c.Code == code, ct).ConfigureAwait(false)) throw Fail.Conflict(ErrorCodes.DuplicateKey, code);
        var c = new CostCentre { Code = code, Name = name };
        db.Set<CostCentre>().Add(c);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(c);
    }

    public async Task<CostCentreResponse> UpdateCostCentreAsync(string token, CostCentreRequest r, CancellationToken ct)
    {
        var (code, name) = ValidateCostCentre(r);
        var c = await FindAsync<CostCentre>(token, ct).ConfigureAwait(false);
        if (code != c.Code && await db.Set<CostCentre>().AnyAsync(x => x.Code == code && x.Id != c.Id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, code);
        db.ExpectRowVersion(c, r.RowVersion);
        c.Code = code;
        c.Name = name;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(c);
    }

    public async Task<CostCentreResponse> SetCostCentreStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var c = await FindAsync<CostCentre>(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(c, rowVersion);
        c.Status = active ? AccountingCodes.Active : AccountingCodes.Archived;
        changes.SetAction(c, active ? "Restore" : "Archive");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(c);
    }

    public async Task DeleteCostCentreAsync(string token, string? reason, CancellationToken ct)
    {
        var why = Reason(reason);
        var c = await FindAsync<CostCentre>(token, ct).ConfigureAwait(false);
        var uses = new List<(string, int)>();
        Add(uses, "EntryLines", await db.Set<EntryLine>().CountAsync(l => l.CostCentreId == c.Id, ct).ConfigureAwait(false));
        Add(uses, "ApArDocumentLines", await db.Set<ApArDocumentLine>().CountAsync(l => l.CostCentreId == c.Id, ct).ConfigureAwait(false));
        if (uses.Count > 0) throw Fail.Rule(ErrorCodes.RecordInUse, accounts.Describe(uses));
        c.DeleteReason = why;
        db.Set<CostCentre>().Remove(c);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static (string Code, string Name) ValidateCostCentre(CostCentreRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var code = TextNormalizer.CleanCode(r.Code);
        var name = TextNormalizer.CleanText(r.Name);
        new Validator()
            .Require("code", code).Check(code is null || ChartOfAccountsService.IsCode(code), "code")
            .Require("name", name).MaxLength("name", name, 150)
            .ThrowIfInvalid();
        return (code!, name!);
    }

    private CostCentreResponse ToResponse(CostCentre c) =>
        new(tokens.Encode<CostCentre>(c.PublicId), c.Code, c.Name, c.Status, Convert.ToBase64String(c.RowVersion));

    // ============================================================ cash boxes

    /// <summary>Active cash boxes and bank accounts for the cash documents' pick lists (receipts, advances, settlements, payments).</summary>
    public async Task<MoneyAccountOptions> MoneyOptionsAsync(CancellationToken ct)
    {
        var boxes = await db.Set<CashBox>().AsNoTracking().Where(c => c.Status == AccountingCodes.Active).OrderBy(c => c.Name)
            .Select(c => new { c.PublicId, c.Name }).ToListAsync(ct).ConfigureAwait(false);
        var banks = await db.Set<BankAccount>().AsNoTracking().Where(b => b.Status == AccountingCodes.Active).OrderBy(b => b.Name)
            .Select(b => new { b.PublicId, b.Name, b.BankName }).ToListAsync(ct).ConfigureAwait(false);
        return new MoneyAccountOptions(boxes.Select(c => new MasterOption(tokens.Encode<CashBox>(c.PublicId), c.Name)).ToList(),
            banks.Select(b => new MasterOption(tokens.Encode<BankAccount>(b.PublicId), b.Name == b.BankName ? b.Name : b.BankName + " · " + b.Name)).ToList(),
            await support.TodayAsync(ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<CashBoxResponse>> CashBoxesAsync(CancellationToken ct) =>
        (await db.Set<CashBox>().AsNoTracking().Include(c => c.Account).OrderBy(c => c.Name).ToListAsync(ct).ConfigureAwait(false))
            .Select(ToResponse).ToList();

    public async Task<CashBoxResponse> CreateCashBoxAsync(CashBoxRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = ValidateName(r.Name, "name");
        var account = await LinkableAccountAsync(r.AccountId, null, null, ct).ConfigureAwait(false);
        var c = new CashBox { Name = name, AccountId = account.Id };
        db.Set<CashBox>().Add(c);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(c, account);
    }

    public async Task<CashBoxResponse> UpdateCashBoxAsync(string token, CashBoxRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = ValidateName(r.Name, "name");
        var c = await FindAsync<CashBox>(token, ct).ConfigureAwait(false);
        var account = await LinkableAccountAsync(r.AccountId, c.Id, null, ct).ConfigureAwait(false);
        db.ExpectRowVersion(c, r.RowVersion);
        c.Name = name;
        c.AccountId = account.Id;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(c, account);
    }

    public async Task<CashBoxResponse> SetCashBoxStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var c = await FindAsync<CashBox>(token, ct).ConfigureAwait(false);
        var account = await db.Set<Account>().AsNoTracking().SingleAsync(a => a.Id == c.AccountId, ct).ConfigureAwait(false);
        if (active && account.Status != AccountingCodes.Active) throw Fail.Rule(ErrorCodes.RecordArchived, account.Code);
        db.ExpectRowVersion(c, rowVersion);
        c.Status = active ? AccountingCodes.Active : AccountingCodes.Archived;
        changes.SetAction(c, active ? "Restore" : "Archive");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(c, account);
    }

    public async Task DeleteCashBoxAsync(string token, string? reason, CancellationToken ct)
    {
        var why = Reason(reason);
        var c = await FindAsync<CashBox>(token, ct).ConfigureAwait(false);
        var uses = new List<(string, int)>();
        Add(uses, "Receipts", await db.Set<Receipt>().CountAsync(x => x.CashBoxId == c.Id, ct).ConfigureAwait(false));
        Add(uses, "Advances", await db.Set<Advance>().CountAsync(x => x.CashBoxId == c.Id, ct).ConfigureAwait(false));
        Add(uses, "Settlements", await db.Set<Settlement>().CountAsync(x => x.CashBoxId == c.Id, ct).ConfigureAwait(false));
        Add(uses, "PartnerPayments", await db.Set<PartnerPayment>().CountAsync(x => x.CashBoxId == c.Id, ct).ConfigureAwait(false));
        Add(uses, "ApArPayments", await db.Set<ApArPayment>().CountAsync(x => x.CashBoxId == c.Id, ct).ConfigureAwait(false));
        if (uses.Count > 0) throw Fail.Rule(ErrorCodes.RecordInUse, DescribeDocuments(uses));
        c.DeleteReason = why;
        db.Set<CashBox>().Remove(c);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private CashBoxResponse ToResponse(CashBox c) => ToResponse(c, c.Account);

    private CashBoxResponse ToResponse(CashBox c, Account account) =>
        new(tokens.Encode<CashBox>(c.PublicId), c.Name, accounts.Ref(account), c.Status, Convert.ToBase64String(c.RowVersion));

    // ============================================================ bank accounts

    public async Task<IReadOnlyList<BankAccountResponse>> BankAccountsAsync(CancellationToken ct) =>
        (await db.Set<BankAccount>().AsNoTracking().Include(b => b.Account).OrderBy(b => b.Name).ToListAsync(ct).ConfigureAwait(false))
            .Select(ToResponse).ToList();

    public async Task<BankAccountResponse> CreateBankAccountAsync(BankAccountRequest r, CancellationToken ct)
    {
        var b = new BankAccount();
        var account = await ApplyAsync(b, r, ct).ConfigureAwait(false);
        db.Set<BankAccount>().Add(b);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(b, account);
    }

    public async Task<BankAccountResponse> UpdateBankAccountAsync(string token, BankAccountRequest r, CancellationToken ct)
    {
        var b = await FindAsync<BankAccount>(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(b, r?.RowVersion);
        var account = await ApplyAsync(b, r, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(b, account);
    }

    public async Task<BankAccountResponse> SetBankAccountStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var b = await FindAsync<BankAccount>(token, ct).ConfigureAwait(false);
        var account = await db.Set<Account>().AsNoTracking().SingleAsync(a => a.Id == b.AccountId, ct).ConfigureAwait(false);
        if (active && account.Status != AccountingCodes.Active) throw Fail.Rule(ErrorCodes.RecordArchived, account.Code);
        db.ExpectRowVersion(b, rowVersion);
        b.Status = active ? AccountingCodes.Active : AccountingCodes.Archived;
        changes.SetAction(b, active ? "Restore" : "Archive");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(b, account);
    }

    public async Task DeleteBankAccountAsync(string token, string? reason, CancellationToken ct)
    {
        var why = Reason(reason);
        var b = await FindAsync<BankAccount>(token, ct).ConfigureAwait(false);
        var uses = new List<(string, int)>();
        Add(uses, "Receipts", await db.Set<Receipt>().CountAsync(x => x.BankAccountId == b.Id, ct).ConfigureAwait(false));
        Add(uses, "Advances", await db.Set<Advance>().CountAsync(x => x.BankAccountId == b.Id, ct).ConfigureAwait(false));
        Add(uses, "Settlements", await db.Set<Settlement>().CountAsync(x => x.BankAccountId == b.Id, ct).ConfigureAwait(false));
        Add(uses, "PartnerPayments", await db.Set<PartnerPayment>().CountAsync(x => x.BankAccountId == b.Id, ct).ConfigureAwait(false));
        Add(uses, "ApArPayments", await db.Set<ApArPayment>().CountAsync(x => x.BankAccountId == b.Id, ct).ConfigureAwait(false));
        if (uses.Count > 0) throw Fail.Rule(ErrorCodes.RecordInUse, DescribeDocuments(uses));
        b.DeleteReason = why;
        db.Set<BankAccount>().Remove(b);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Validates and applies the request; returns the (untracked) linked account for the response.</summary>
    private async Task<Account> ApplyAsync(BankAccount b, BankAccountRequest? r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = TextNormalizer.CleanText(r.Name);
        var bankName = TextNormalizer.CleanText(r.BankName);
        var number = TextNormalizer.CleanCode(r.AccountNumber)?.Replace(" ", string.Empty, StringComparison.Ordinal);
        var iban = TextNormalizer.CleanCode(r.Iban)?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var currency = TextNormalizer.CleanCode(r.CurrencyCode)?.ToUpperInvariant()
            ?? await db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => c.CurrencyCode).SingleAsync(ct).ConfigureAwait(false);
        var ibanPattern = await db.Set<CompanySetting>().AsNoTracking().Where(s => s.SettingKey == SettingKeys.IbanPattern)
            .Select(s => s.SettingValue).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? SettingKeys.JordanIbanPattern;
        new Validator()
            .Require("name", name).MaxLength("name", name, 100)
            .Require("bankName", bankName).MaxLength("bankName", bankName, 100)
            .Check(number is null || AccountNumberPattern().IsMatch(number), "accountNumber")
            .Check(iban is null || Regex.IsMatch(iban, ibanPattern, RegexOptions.None, TimeSpan.FromSeconds(1)), "iban")
            .Check(CurrencyPattern().IsMatch(currency), "currencyCode")
            .ThrowIfInvalid();
        var account = await LinkableAccountAsync(r.AccountId, null, b.Id == 0 ? null : b.Id, ct).ConfigureAwait(false);
        b.Name = name!;
        b.BankName = bankName!;
        b.AccountNumber = number;
        b.Iban = iban;
        b.CurrencyCode = currency;
        b.AccountId = account.Id;
        return account;
    }

    private BankAccountResponse ToResponse(BankAccount b) => ToResponse(b, b.Account);

    private BankAccountResponse ToResponse(BankAccount b, Account account) =>
        new(tokens.Encode<BankAccount>(b.PublicId), b.Name, b.BankName, b.AccountNumber, b.Iban, b.CurrencyCode, accounts.Ref(account),
            b.Status, Convert.ToBase64String(b.RowVersion));

    [GeneratedRegex("^[A-Za-z0-9-]{1,40}$")]
    private static partial Regex AccountNumberPattern();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();

    // ============================================================ shared

    /// <summary>A postable, active asset leaf account not linked to another cash box or bank account (DT-25).</summary>
    private async Task<Account> LinkableAccountAsync(string? token, long? exceptCashBoxId, long? exceptBankId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token)) throw Fail.Field("accountId", ErrorCodes.ValidationRequired, "accountId");
        var account = await accounts.FindAsync(token, tracked: false, ct).ConfigureAwait(false);
        var hasChildren = await db.Set<Account>().AnyAsync(a => a.ParentId == account.Id, ct).ConfigureAwait(false);
        if (account.Status != AccountingCodes.Active || !account.IsPostable || hasChildren)
            throw Fail.RuleOn("accountId", ErrorCodes.AccountNotPostable, account.Code);
        if (account.AccountType != "Asset" || account.IsControlAccount) throw Fail.RuleOn("accountId", ErrorCodes.AccountNotAsset);
        var taken = await db.Set<CashBox>().AnyAsync(c => c.AccountId == account.Id && c.Id != exceptCashBoxId, ct).ConfigureAwait(false)
            || await db.Set<BankAccount>().AnyAsync(b => b.AccountId == account.Id && b.Id != exceptBankId, ct).ConfigureAwait(false);
        if (taken) throw Fail.RuleOn("accountId", ErrorCodes.AccountLinkTaken, account.Code);
        return account;
    }

    private async Task<T> FindAsync<T>(string token, CancellationToken ct) where T : Domain.Common.AuditableEntity
    {
        var publicId = tokens.Decode<T>(token) ?? throw Fail.NotFound();
        return await db.Set<T>().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }

    private static string ValidateName(string? value, string field)
    {
        var name = TextNormalizer.CleanText(value);
        new Validator().Require(field, name).MaxLength(field, name, 100).ThrowIfInvalid();
        return name!;
    }

    private static string Reason(string? reason)
    {
        var why = TextNormalizer.CleanText(reason) ?? throw Fail.Field("reason", ErrorCodes.ReasonRequired);
        return why.Length <= 500 ? why : why[..500];
    }

    private static void Add(List<(string, int)> uses, string type, int count)
    {
        if (count > 0) uses.Add((type, count));
    }

    private string DescribeDocuments(List<(string Type, int Count)> uses) => accounts.Describe(uses);
}
