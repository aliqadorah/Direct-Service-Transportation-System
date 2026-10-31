using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Chart of accounts (acc.acct.setup to change, acc.acct.view to read; FR-107, FR-108, FR-093): tree with add, edit,
/// move, deactivate/activate and delete of unreferenced accounts (soft, BR-040/041), and the built-in transport
/// template (D-10). Rules: a child has its parent's type; only leaf accounts are postable; an account that carries
/// entries, openings or links cannot receive children or stop being postable; a mapped account cannot be deactivated.
/// </summary>
public sealed partial class ChartOfAccountsService(IAppDbContext db, IUrlTokenService tokens, IChangeContext changes, IRequestContext ctx)
{
    // ------------------------------------------------------------ queries

    public async Task<IReadOnlyList<AccountResponse>> ListAsync(string? status, CancellationToken ct)
    {
        var all = await db.Set<Account>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var byId = all.ToDictionary(a => a.Id);
        var withChildren = all.Where(a => a.ParentId is not null).Select(a => a.ParentId!.Value).ToHashSet();
        return all
            .Where(a => string.IsNullOrEmpty(status) || a.Status == status)
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .Select(a => ToResponse(a, byId, withChildren))
            .ToList();
    }

    /// <summary>
    /// Active postable accounts for the pick lists of journal lines, AP/AR lines, cash boxes, banks and account mapping (FR-106, FR-123):
    /// code, names, type, control flag and party type, so forms can ask for a party on control accounts.
    /// </summary>
    public async Task<IReadOnlyList<AccountOption>> OptionsAsync(CancellationToken ct) =>
        (await db.Set<Account>().AsNoTracking().Where(a => a.IsPostable && a.Status == AccountingCodes.Active).OrderBy(a => a.Code).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => new AccountOption(Ref(a), a.AccountType, a.IsControlAccount, a.ControlPartyType, a.IsTaxAccount)).ToList();

    public async Task<AccountResponse> GetAsync(string token, CancellationToken ct)
    {
        var a = await FindAsync(token, tracked: false, ct).ConfigureAwait(false);
        return await ResponseAsync(a, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ commands

    public async Task<AccountResponse> CreateAsync(AccountRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var (code, nameAr, nameEn) = ValidateBasics(r);
        Account? parent = null;
        if (!string.IsNullOrEmpty(r.ParentId))
        {
            parent = await FindAsync(r.ParentId, tracked: true, ct).ConfigureAwait(false);
            await EnsureCanHaveChildrenAsync(parent, ct).ConfigureAwait(false);
        }
        var type = r.AccountType ?? parent?.AccountType;
        new Validator().Check(type is not null && AccountingCodes.AccountTypes.Contains(type), "accountType").ThrowIfInvalid();
        if (parent is not null && parent.AccountType != type) throw Fail.RuleOn("accountType", ErrorCodes.AccountTypeMismatch, parent.AccountType);
        var (isControl, partyType) = ValidateControl(r);
        var plGroup = ValidateProfitLossGroup(r, type!);
        await EnsureUniqueCodeAsync(code, null, ct).ConfigureAwait(false);

        var account = new Account
        {
            Code = code, NameAr = nameAr, NameEn = nameEn, AccountType = type!, ParentId = parent?.Id, IsPostable = r.IsPostable,
            IsControlAccount = isControl, ControlPartyType = partyType, Status = AccountingCodes.Active, IsTaxAccount = r.IsTaxAccount,
            ProfitLossGroup = plGroup,
        };
        if (parent is not null && parent.IsPostable)
        {
            parent.IsPostable = false; // FR-108: an account with children is not postable
            changes.SetAction(parent, "Update");
        }
        db.Set<Account>().Add(account);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(account, ct).ConfigureAwait(false);
    }

    public async Task<AccountResponse> UpdateAsync(string token, AccountRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var a = await FindAsync(token, tracked: true, ct).ConfigureAwait(false);
        var (code, nameAr, nameEn) = ValidateBasics(r);
        var type = r.AccountType ?? a.AccountType;
        new Validator().Check(AccountingCodes.AccountTypes.Contains(type), "accountType").ThrowIfInvalid();
        var (isControl, partyType) = ValidateControl(r);
        var plGroup = ValidateProfitLossGroup(r, type);
        var hasChildren = await db.Set<Account>().AnyAsync(x => x.ParentId == a.Id, ct).ConfigureAwait(false);

        if (type != a.AccountType)
        {
            if (a.ParentId is long pid)
            {
                var parentType = await db.Set<Account>().Where(x => x.Id == pid).Select(x => x.AccountType).SingleAsync(ct).ConfigureAwait(false);
                if (parentType != type) throw Fail.RuleOn("accountType", ErrorCodes.AccountTypeMismatch, parentType);
            }
            if (hasChildren) throw Fail.RuleOn("accountType", ErrorCodes.AccountHasChildren, a.Code);
            await EnsureNoEntriesAsync(a, ct).ConfigureAwait(false);
        }
        if (r.IsPostable && !a.IsPostable && hasChildren) throw Fail.RuleOn("isPostable", ErrorCodes.AccountHasChildren, a.Code);
        if (!r.IsPostable && a.IsPostable)
        {
            var uses = await UsesAsync(a, ct).ConfigureAwait(false);
            if (uses.Count > 0) throw Fail.Rule(ErrorCodes.RecordInUse, Describe(uses));
        }
        if (isControl != a.IsControlAccount || partyType != a.ControlPartyType)
            await EnsureNoEntriesAsync(a, ct).ConfigureAwait(false); // party rules apply to every line of the account
        if (code != a.Code) await EnsureUniqueCodeAsync(code, a.Id, ct).ConfigureAwait(false);

        db.ExpectRowVersion(a, r.RowVersion);
        a.Code = code;
        a.NameAr = nameAr;
        a.NameEn = nameEn;
        a.AccountType = type;
        a.IsPostable = r.IsPostable;
        a.IsControlAccount = isControl;
        a.ControlPartyType = partyType;
        a.IsTaxAccount = r.IsTaxAccount;
        a.ProfitLossGroup = plGroup;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(a, ct).ConfigureAwait(false);
    }

    /// <summary>Moves an account (with its sub-tree) under another parent or to the top level.</summary>
    public async Task<AccountResponse> MoveAsync(string token, MoveAccountRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var a = await FindAsync(token, tracked: true, ct).ConfigureAwait(false);
        Account? parent = null;
        if (!string.IsNullOrEmpty(r.ParentId))
        {
            parent = await FindAsync(r.ParentId, tracked: true, ct).ConfigureAwait(false);
            var parentOf = await db.Set<Account>().AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.ParentId, ct).ConfigureAwait(false);
            for (long? cursor = parent.Id; cursor is long id; cursor = parentOf.GetValueOrDefault(id))
            {
                if (id == a.Id) throw Fail.Rule(ErrorCodes.AccountCycle);
            }
            if (parent.AccountType != a.AccountType) throw Fail.Rule(ErrorCodes.AccountTypeMismatch, parent.AccountType);
            await EnsureCanHaveChildrenAsync(parent, ct).ConfigureAwait(false);
        }
        db.ExpectRowVersion(a, r.RowVersion);
        a.ParentId = parent?.Id;
        changes.SetAction(a, "Move");
        if (parent is { IsPostable: true })
        {
            parent.IsPostable = false;
            changes.SetAction(parent, "Update");
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(a, ct).ConfigureAwait(false);
    }

    /// <summary>FR-093/FR-108: deactivation (Archived). Refused while mapped or linked to a cash box/bank account, or with active children.</summary>
    public async Task<AccountResponse> DeactivateAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var a = await FindAsync(token, tracked: true, ct).ConfigureAwait(false);
        var linked = await db.Set<PostingKeyMapping>().AnyAsync(m => m.AccountId == a.Id, ct).ConfigureAwait(false)
            || await db.Set<CashBox>().AnyAsync(c => c.AccountId == a.Id && c.Status == AccountingCodes.Active, ct).ConfigureAwait(false)
            || await db.Set<BankAccount>().AnyAsync(b => b.AccountId == a.Id && b.Status == AccountingCodes.Active, ct).ConfigureAwait(false);
        if (linked) throw Fail.Rule(ErrorCodes.AccountMapped, a.Code);
        if (a.OpeningDebit > 0 || a.OpeningCredit > 0) throw Fail.Rule(ErrorCodes.RecordInUse, Describe([("OpeningBalance", 1)]));
        if (await db.Set<Account>().AnyAsync(x => x.ParentId == a.Id && x.Status == AccountingCodes.Active, ct).ConfigureAwait(false))
            throw Fail.Rule(ErrorCodes.AccountActiveChildren, a.Code);
        db.ExpectRowVersion(a, rowVersion);
        a.Status = AccountingCodes.Archived;
        changes.SetAction(a, "Deactivate");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(a, ct).ConfigureAwait(false);
    }

    public async Task<AccountResponse> ActivateAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var a = await FindAsync(token, tracked: true, ct).ConfigureAwait(false);
        if (a.ParentId is long pid)
        {
            var parent = await db.Set<Account>().AsNoTracking().SingleAsync(x => x.Id == pid, ct).ConfigureAwait(false);
            if (parent.Status != AccountingCodes.Active) throw Fail.Rule(ErrorCodes.RecordArchived, parent.Code);
        }
        db.ExpectRowVersion(a, rowVersion);
        a.Status = AccountingCodes.Active;
        changes.SetAction(a, "Activate");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(a, ct).ConfigureAwait(false);
    }

    /// <summary>Soft delete of an unreferenced account (BR-040, BR-041); the refusal lists each referencing type with its count.</summary>
    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var why = TextNormalizer.CleanText(reason);
        if (why is null) throw Fail.Field("reason", ErrorCodes.ReasonRequired);
        var a = await FindAsync(token, tracked: true, ct).ConfigureAwait(false);
        var uses = await UsesAsync(a, ct).ConfigureAwait(false);
        var children = await db.Set<Account>().CountAsync(x => x.ParentId == a.Id, ct).ConfigureAwait(false);
        if (children > 0) uses.Insert(0, ("SubAccounts", children));
        if (uses.Count > 0) throw Fail.Rule(ErrorCodes.RecordInUse, Describe(uses));
        a.DeleteReason = why.Length <= 500 ? why : why[..500];
        db.Set<Account>().Remove(a);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Creates the approved transport template (D-10) in an empty chart: accounts, main cash box, posting-key mapping.</summary>
    public async Task<ChartTemplateResult> ApplyTemplateAsync(CancellationToken ct)
    {
        if (await db.Set<Account>().AnyAsync(ct).ConfigureAwait(false)) throw Fail.Conflict(ErrorCodes.ChartNotEmpty);
        return await db.InTransactionAsync(async c =>
        {
            var created = new Dictionary<string, Account>(StringComparer.Ordinal);
            foreach (var t in ChartTemplate.Accounts)
            {
                var account = new Account
                {
                    Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn, AccountType = t.Type,
                    Parent = t.ParentCode is null ? null : created[t.ParentCode],
                    IsPostable = ChartTemplate.IsLeaf(t.Code),
                    IsControlAccount = t.ControlPartyType is not null, ControlPartyType = t.ControlPartyType,
                    Status = AccountingCodes.Active, IsTaxAccount = t.IsTax, ProfitLossGroup = t.ProfitLossGroup,
                };
                created[t.Code] = account;
                db.Set<Account>().Add(account);
                changes.SetAction(account, "Template");
            }
            await db.SaveChangesAsync(c).ConfigureAwait(false);

            var cashBoxes = 0;
            if (!await db.Set<CashBox>().AnyAsync(c).ConfigureAwait(false))
            {
                db.Set<CashBox>().Add(new CashBox { Name = ChartTemplate.MainCashBoxNameAr, AccountId = created[ChartTemplate.MainCashBoxAccountCode].Id });
                cashBoxes = 1;
            }
            foreach (var (key, code) in ChartTemplate.Mapping)
                db.Set<PostingKeyMapping>().Add(new PostingKeyMapping { PostingKey = key, AccountId = created[code].Id });
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return new ChartTemplateResult(created.Count, cashBoxes, ChartTemplate.Mapping.Count);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// "Add the default tax accounts" (DT-256, existing companies — nothing is added automatically): for each of VAT on sales, VAT on purchases
    /// and VAT expenses whose type has no active tax account yet, the template account with its code is marked as a tax account when it is a
    /// plain active leaf of that type; otherwise a new account is added under the template group (or the first top-level account of the type
    /// that can take sub-accounts) with the next free code of that group.
    /// </summary>
    public async Task<DefaultTaxAccountsResult> AddDefaultTaxAccountsAsync(CancellationToken ct) =>
        await db.InTransactionAsync(async c =>
        {
            var all = await db.Set<Account>().ToListAsync(c).ConfigureAwait(false);
            var parents = all.Where(a => a.ParentId is not null).Select(a => a.ParentId!.Value).ToHashSet();
            var codes = all.Select(a => a.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var touched = new List<Account>();
            int added = 0, marked = 0;
            foreach (var d in ChartTemplate.TaxAccounts)
            {
                if (all.Any(a => a.IsTaxAccount && a.AccountType == d.Type && a.Status == AccountingCodes.Active)) continue;
                // Only the template's own account (same code and type, one of its names): an unrelated account that took the code stays as it is.
                var names = ChartTemplate.Accounts.Where(t => t.Code == d.TemplateCode).SelectMany(t => new[] { t.NameAr, t.NameEn }).Append(d.NameAr).Append(d.NameEn)
                    .Concat(ChartTemplate.FormerNamesAr).ToList();
                var template = all.FirstOrDefault(a => a.Code == d.TemplateCode && a.AccountType == d.Type && !a.IsControlAccount
                    && a.Status == AccountingCodes.Active && a.IsPostable && !parents.Contains(a.Id) && (names.Contains(a.NameAr) || (a.NameEn is not null && names.Contains(a.NameEn))));
                if (template is not null)
                {
                    template.IsTaxAccount = true;
                    touched.Add(template);
                    marked++;
                    continue;
                }
                Account? parent = null;
                string? code = null;
                foreach (var candidate in all.Where(a => a.AccountType == d.Type && a.Status == AccountingCodes.Active && (a.Code == d.GroupCode || a.ParentId is null))
                             .OrderBy(a => a.Code == d.GroupCode ? 0 : 1).ThenBy(a => a.Code, StringComparer.Ordinal))
                {
                    if ((await UsesAsync(candidate, c).ConfigureAwait(false)).Count > 0) continue;
                    code = Enumerable.Range(1, 99).Select(n => candidate.Code + n.ToString("00", System.Globalization.CultureInfo.InvariantCulture))
                        .FirstOrDefault(x => !codes.Contains(x) && IsCode(x));
                    if (code is null) continue;
                    parent = candidate;
                    break;
                }
                if (parent is null || code is null) throw Fail.Rule(ErrorCodes.TaxAccountNoGroup, TypeName(d.Type));
                if (parent.IsPostable)
                {
                    parent.IsPostable = false; // FR-108: an account with children is not postable
                    changes.SetAction(parent, "Update");
                }
                var account = new Account
                {
                    Code = code, NameAr = d.NameAr, NameEn = d.NameEn, AccountType = d.Type, Parent = parent, IsPostable = true,
                    Status = AccountingCodes.Active, IsTaxAccount = true,
                };
                db.Set<Account>().Add(account);
                all.Add(account);
                codes.Add(code);
                touched.Add(account);
                added++;
            }
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return new DefaultTaxAccountsResult(added, marked, touched.Select(Ref).ToList());
        }, ct).ConfigureAwait(false);

    private string TypeName(string type) => (ctx.Language == "en", type) switch
    {
        (true, "Liability") => "Liabilities",
        (false, "Liability") => "الالتزامات",
        (true, "Asset") => "Assets",
        (false, "Asset") => "الأصول",
        (true, "Expense") => "Expenses",
        (false, "Expense") => "المصروفات",
        _ => type,
    };

    // ------------------------------------------------------------ shared rules

    /// <summary>What refers to an account (BR-040 list for acc.Accounts), with counts. Children are checked separately.</summary>
    internal async Task<List<(string Type, int Count)>> UsesAsync(Account a, CancellationToken ct)
    {
        var uses = new List<(string, int)>();
        void Add(string type, int count) { if (count > 0) uses.Add((type, count)); }
        Add("EntryLines", await db.Set<EntryLine>().CountAsync(l => l.AccountId == a.Id, ct).ConfigureAwait(false));
        Add("PostingKeys", await db.Set<PostingKeyMapping>().CountAsync(m => m.AccountId == a.Id, ct).ConfigureAwait(false));
        Add("CashBoxes", await db.Set<CashBox>().CountAsync(x => x.AccountId == a.Id, ct).ConfigureAwait(false));
        Add("BankAccounts", await db.Set<BankAccount>().CountAsync(x => x.AccountId == a.Id, ct).ConfigureAwait(false));
        Add("ApArParties", await db.Set<ApArParty>().CountAsync(x => x.ControlAccountId == a.Id, ct).ConfigureAwait(false));
        Add("ApArDocumentLines", await db.Set<ApArDocumentLine>().CountAsync(x => x.AccountId == a.Id, ct).ConfigureAwait(false));
        if (a.OpeningDebit > 0 || a.OpeningCredit > 0) uses.Add(("OpeningBalance", 1));
        return uses;
    }

    private async Task EnsureCanHaveChildrenAsync(Account parent, CancellationToken ct)
    {
        if (parent.Status != AccountingCodes.Active) throw Fail.Rule(ErrorCodes.RecordArchived, parent.Code);
        if ((await UsesAsync(parent, ct).ConfigureAwait(false)).Count > 0) throw Fail.Rule(ErrorCodes.AccountParentInUse, parent.Code);
    }

    private async Task EnsureNoEntriesAsync(Account a, CancellationToken ct)
    {
        var lines = await db.Set<EntryLine>().CountAsync(l => l.AccountId == a.Id, ct).ConfigureAwait(false);
        if (lines > 0) throw Fail.Rule(ErrorCodes.RecordInUse, Describe([("EntryLines", lines)]));
    }

    private async Task EnsureUniqueCodeAsync(string code, long? exceptId, CancellationToken ct)
    {
        if (await db.Set<Account>().AnyAsync(a => a.Code == code && a.Id != exceptId, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, code);
    }

    private static (string Code, string NameAr, string? NameEn) ValidateBasics(AccountRequest r)
    {
        var code = TextNormalizer.CleanCode(r.Code);
        var nameAr = TextNormalizer.CleanText(r.NameAr);
        var nameEn = TextNormalizer.CleanText(r.NameEn);
        new Validator()
            .Require("code", code).Check(code is null || IsCode(code), "code")
            .Require("nameAr", nameAr).MaxLength("nameAr", nameAr, 200)
            .MaxLength("nameEn", nameEn, 200)
            .ThrowIfInvalid();
        return (code!, nameAr!, nameEn);
    }

    /// <summary>The profit and loss section (DT-264): empty = inherited; otherwise one of the sections of the account type (revenue / expense only).</summary>
    private static string? ValidateProfitLossGroup(AccountRequest r, string type)
    {
        var group = string.IsNullOrWhiteSpace(r.ProfitLossGroup) ? null : r.ProfitLossGroup.Trim();
        if (group is not null && !ProfitLossGroups.For(type).Contains(group)) throw Fail.Field("profitLossGroup", ErrorCodes.ValidationFormat, new FieldRef("profitLossGroup"));
        return group;
    }

    private static (bool IsControl, string? PartyType) ValidateControl(AccountRequest r)
    {
        // DT-256: a tax account carries the automatic tax lines, which have no party.
        if (r.IsTaxAccount && r.IsControlAccount) throw Fail.RuleOn("isTaxAccount", ErrorCodes.TaxAccountControl);
        if (!r.IsControlAccount) return (false, null);
        new Validator().Check(r.ControlPartyType is not null && AccountingCodes.ControlPartyTypes.Contains(r.ControlPartyType), "controlPartyType")
            .ThrowIfInvalid();
        return (true, r.ControlPartyType);
    }

    /// <summary>Account codes: 1–30 letters, digits, dot, dash or underscore (VARCHAR(30)).</summary>
    public static bool IsCode(string code) => CodePattern().IsMatch(code);

    [GeneratedRegex("^[A-Za-z0-9._-]{1,30}$")]
    private static partial Regex CodePattern();

    internal string Describe(IEnumerable<(string Type, int Count)> uses) =>
        string.Join(ctx.Language == "en" ? ", " : "، ", uses.Select(u => $"{UseName(u.Type)} ({u.Count})"));

    private string UseName(string type) => (ctx.Language == "en", type) switch
    {
        (true, "EntryLines") => "journal lines",
        (false, "EntryLines") => "سطور قيود",
        (true, "PostingKeys") => "posting keys",
        (false, "PostingKeys") => "مفاتيح ترحيل",
        (true, "CashBoxes") => "cash boxes",
        (false, "CashBoxes") => "صناديق",
        (true, "BankAccounts") => "bank accounts",
        (false, "BankAccounts") => "حسابات بنكية",
        (true, "ApArParties") => "accounting parties",
        (false, "ApArParties") => "أطراف محاسبية",
        (true, "ApArDocumentLines") => "payable/receivable documents",
        (false, "ApArDocumentLines") => "مستندات ذمم",
        (true, "OpeningBalance") => "opening balance",
        (false, "OpeningBalance") => "رصيد افتتاحي",
        (true, "SubAccounts") => "sub-accounts",
        (false, "SubAccounts") => "حسابات فرعية",
        (true, "Receipts") => "receipts",
        (false, "Receipts") => "سندات قبض",
        (true, "Advances") => "driver advances",
        (false, "Advances") => "سلف سائقين",
        (true, "Settlements") => "driver settlements",
        (false, "Settlements") => "تسويات سائقين",
        (true, "PartnerPayments") => "partner payments",
        (false, "PartnerPayments") => "دفعات شركاء",
        (true, "ApArPayments") => "payments and collections",
        (false, "ApArPayments") => "دفعات وتحصيلات",
        (true, _) => type,
        _ => type,
    };

    internal async Task<Account> FindAsync(string? token, bool tracked, CancellationToken ct)
    {
        var publicId = tokens.Decode<Account>(token) ?? throw Fail.NotFound();
        var q = tracked ? db.Set<Account>() : db.Set<Account>().AsNoTracking();
        return await q.SingleOrDefaultAsync(a => a.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }

    private async Task<AccountResponse> ResponseAsync(Account a, CancellationToken ct)
    {
        var all = await db.Set<Account>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var byId = all.ToDictionary(x => x.Id);
        byId[a.Id] = a;
        var withChildren = all.Where(x => x.ParentId is not null).Select(x => x.ParentId!.Value).ToHashSet();
        return ToResponse(a, byId, withChildren);
    }

    private AccountResponse ToResponse(Account a, Dictionary<long, Account> byId, HashSet<long> withChildren)
    {
        var level = 1;
        for (var p = a.ParentId; p is long id && byId.TryGetValue(id, out var parent) && level < 50; p = parent.ParentId) level++;
        var parentAccount = a.ParentId is long pid ? byId.GetValueOrDefault(pid) : null;
        return new AccountResponse(tokens.Encode<Account>(a.PublicId), a.Code, a.NameAr, a.NameEn, a.AccountType,
            parentAccount is null ? null : tokens.Encode<Account>(parentAccount.PublicId), parentAccount?.Code, level,
            a.IsPostable, withChildren.Contains(a.Id), a.OpeningDebit, a.OpeningCredit, a.IsControlAccount, a.ControlPartyType, a.Status,
            Convert.ToBase64String(a.RowVersion), a.IsTaxAccount, a.ProfitLossGroup);
    }

    internal AccountRef Ref(Account a) => new(tokens.Encode<Account>(a.PublicId), a.Code, a.NameAr, a.NameEn);
}
