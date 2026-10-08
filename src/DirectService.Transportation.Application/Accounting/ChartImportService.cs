using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Chart of accounts import/export (FR-109, ADR-13): Excel with separate debit and credit opening-balance columns.
/// The preview rejects duplicate codes (in the file or already in the chart, DT-26), unknown parents, invalid types and
/// other rule breaks; the import is strict (all rows or none) and logged in cfg.ImportBatches. Openings imported here
/// are posted with the opening-balances action (FR-110).
/// </summary>
public sealed class ChartImportService(IAppDbContext db, ISpreadsheetService sheets, IRequestContext ctx, IUrlTokenService tokens,
    ChartOfAccountsService accounts, PostingEngine engine)
{
    public const string EntityType = "ACC.Account";

    private static readonly string[] CodeAliases = ["Code", "Account code", "الرمز", "رمز الحساب"];
    private static readonly string[] NameArAliases = ["NameAr", "Arabic name", "الاسم بالعربية", "اسم الحساب"];
    private static readonly string[] NameEnAliases = ["NameEn", "English name", "الاسم بالإنجليزية"];
    private static readonly string[] TypeAliases = ["Type", "Account type", "النوع", "نوع الحساب"];
    private static readonly string[] ParentAliases = ["ParentCode", "Parent code", "Parent", "الحساب الأب", "رمز الحساب الأب"];
    private static readonly string[] PostableAliases = ["Postable", "قابل للترحيل"];
    private static readonly string[] DebitAliases = ["OpeningDebit", "Opening debit", "رصيد افتتاحي مدين", "افتتاحي مدين"];
    private static readonly string[] CreditAliases = ["OpeningCredit", "Opening credit", "رصيد افتتاحي دائن", "افتتاحي دائن"];
    private static readonly string[] ControlAliases = ["ControlPartyType", "Control party type", "نوع طرف الرقابة"];
    private static readonly string[] StatusAliases = ["Status", "الحالة"];

    private static readonly Dictionary<string, string> TypeNames = new(StringComparer.Ordinal)
    {
        [TextNormalizer.Key("Asset")] = "Asset", [TextNormalizer.Key("أصول")] = "Asset", [TextNormalizer.Key("الأصول")] = "Asset",
        [TextNormalizer.Key("Liability")] = "Liability", [TextNormalizer.Key("التزامات")] = "Liability", [TextNormalizer.Key("الالتزامات")] = "Liability",
        [TextNormalizer.Key("خصوم")] = "Liability",
        [TextNormalizer.Key("Equity")] = "Equity", [TextNormalizer.Key("حقوق الملكية")] = "Equity", [TextNormalizer.Key("حقوق ملكية")] = "Equity",
        [TextNormalizer.Key("Revenue")] = "Revenue", [TextNormalizer.Key("إيرادات")] = "Revenue", [TextNormalizer.Key("الإيرادات")] = "Revenue",
        [TextNormalizer.Key("Expense")] = "Expense", [TextNormalizer.Key("مصروفات")] = "Expense", [TextNormalizer.Key("المصروفات")] = "Expense",
    };

    // ------------------------------------------------------------ export

    public async Task<GeneratedFile> ExportAsync(CancellationToken ct)
    {
        var list = await accounts.ListAsync(null, ct).ConfigureAwait(false);
        var en = ctx.Language == "en";
        string[] headers = en
            ? ["Code", "Arabic name", "English name", "Account type", "Parent code", "Postable", "Opening debit", "Opening credit", "Control party type", "Status"]
            : ["رمز الحساب", "الاسم بالعربية", "الاسم بالإنجليزية", "نوع الحساب", "رمز الحساب الأب", "قابل للترحيل", "رصيد افتتاحي مدين", "رصيد افتتاحي دائن", "نوع طرف الرقابة", "الحالة"];
        var rows = list.Select(a => (IReadOnlyList<object?>)
            [a.Code, a.NameAr, a.NameEn, a.AccountType, a.ParentCode, a.IsPostable, a.OpeningDebit, a.OpeningCredit, a.ControlPartyType, a.Status]);
        var bytes = sheets.WriteXlsx(en ? "Chart of accounts" : "دليل الحسابات", headers, rows);
        return new GeneratedFile("chart-of-accounts.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", bytes);
    }

    // ------------------------------------------------------------ import

    private sealed record Row(int RowNumber, string? Code, string? NameAr, string? NameEn, string? Type, string? ParentCode, bool? Postable,
        decimal Debit, decimal Credit, string? ControlPartyType);

    public async Task<ImportPreview> PreviewAsync(Stream content, string fileName, long length, CancellationToken ct)
    {
        var (_, preview) = await AnalyseAsync(content, fileName, length, ct).ConfigureAwait(false);
        return preview;
    }

    public async Task<ImportResult> ImportAsync(Stream content, string fileName, long length, CancellationToken ct)
    {
        var (rows, preview) = await AnalyseAsync(content, fileName, length, ct).ConfigureAwait(false);
        if (preview.RejectedRows > 0) throw Fail.Rule(ErrorCodes.ImportHasErrors, preview.RejectedRows);
        var parentCodes = rows.Where(r => r.ParentCode is not null).Select(r => r.ParentCode!).ToHashSet(StringComparer.Ordinal);

        var batch = await db.InTransactionAsync(async c =>
        {
            var created = new Dictionary<string, Account>(StringComparer.Ordinal);
            var tracked = await db.Set<Account>().Where(a => parentCodes.Contains(a.Code)).ToDictionaryAsync(a => a.Code, StringComparer.Ordinal, c)
                .ConfigureAwait(false);
            foreach (var r in rows)
            {
                var account = new Account
                {
                    Code = r.Code!, NameAr = r.NameAr!, NameEn = r.NameEn, AccountType = r.Type!,
                    IsPostable = r.Postable ?? !parentCodes.Contains(r.Code!),
                    OpeningDebit = r.Debit, OpeningCredit = r.Credit,
                    IsControlAccount = r.ControlPartyType is not null, ControlPartyType = r.ControlPartyType,
                    Status = AccountingCodes.Active,
                };
                created[r.Code!] = account;
                db.Set<Account>().Add(account);
            }
            foreach (var r in rows.Where(r => r.ParentCode is not null))
            {
                if (created.TryGetValue(r.ParentCode!, out var parentInFile))
                {
                    created[r.Code!].Parent = parentInFile;
                }
                else
                {
                    var parent = tracked[r.ParentCode!];
                    created[r.Code!].ParentId = parent.Id;
                    parent.IsPostable = false;
                }
            }
            var b = ImportParse.Batch(EntityType, fileName, rows.Count, rows.Count, ctx.UserId!.Value);
            db.Set<ImportBatch>().Add(b);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return b;
        }, ct).ConfigureAwait(false);
        return new ImportResult(tokens.Encode<ImportBatch>(batch.PublicId), rows.Count, rows.Count);
    }

    private async Task<(List<Row> Rows, ImportPreview Preview)> AnalyseAsync(
        Stream content, string fileName, long length, CancellationToken ct)
    {
        var sheet = sheets.Read(content, fileName, length);
        int code = sheet.Column(CodeAliases), nameAr = sheet.Column(NameArAliases), nameEn = sheet.Column(NameEnAliases),
            type = sheet.Column(TypeAliases), parent = sheet.Column(ParentAliases), postable = sheet.Column(PostableAliases),
            debit = sheet.Column(DebitAliases), credit = sheet.Column(CreditAliases), control = sheet.Column(ControlAliases);
        _ = sheet.Column(StatusAliases); // exported for information; imported accounts are always Active
        ImportParse.RequireColumns(sheet, (code, CodeAliases[0]), (nameAr, NameArAliases[0]), (type, TypeAliases[0]));

        var decimals = await engine.CurrencyDecimalsAsync(ct).ConfigureAwait(false);
        var existing = await db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Code, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var p = new ImportProblems(ctx.Language);
        var rows = new List<Row>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in sheet.Rows)
        {
            var c = TextNormalizer.CleanCode(s[code]);
            var typeText = s[type];
            var t = typeText is null ? null : TypeNames.GetValueOrDefault(TextNormalizer.Key(typeText));
            var postableValue = postable < 0 ? null : ImportParse.Bool(s[postable]);
            var controlType = TextNormalizer.CleanCode(s[control]);
            if (c is null || !ChartOfAccountsService.IsCode(c)) p.Add(s.RowNumber, c, ErrorCodes.ImportInvalid, Label("code"), s[code]);
            else if (!seen.Add(c) || existing.ContainsKey(c)) p.Add(s.RowNumber, c, ErrorCodes.ImportDuplicate, c);
            var nAr = TextNormalizer.CleanText(s[nameAr]);
            if (nAr is null || nAr.Length > 200) p.Add(s.RowNumber, c, ErrorCodes.ImportInvalid, Label("nameAr"), s[nameAr]);
            var nEn = TextNormalizer.CleanText(s[nameEn]);
            if (nEn is { Length: > 200 }) p.Add(s.RowNumber, c, ErrorCodes.ImportInvalid, Label("nameEn"), nEn);
            if (t is null) p.Add(s.RowNumber, c, ErrorCodes.ImportInvalid, Label("type"), typeText);
            if (postable >= 0 && s[postable] is not null && postableValue is null) p.Add(s.RowNumber, c, ErrorCodes.ImportInvalid, Label("postable"), s[postable]);
            if (!ImportParse.TryAmount(s[debit], decimals, out var d)) p.Add(s.RowNumber, c, ErrorCodes.ImportInvalid, Label("debit"), s[debit]);
            if (!ImportParse.TryAmount(s[credit], decimals, out var cr)) p.Add(s.RowNumber, c, ErrorCodes.ImportInvalid, Label("credit"), s[credit]);
            if (d > 0 && cr > 0) p.Add(s.RowNumber, c, ErrorCodes.ImportInvalid, Label("debit"), $"{d} / {cr}");
            if (controlType is not null && !AccountingCodes.ControlPartyTypes.Contains(controlType))
                p.Add(s.RowNumber, c, ErrorCodes.ImportInvalid, Label("control"), controlType);
            rows.Add(new Row(s.RowNumber, c, nAr, nEn, t, TextNormalizer.CleanCode(s[parent]), postableValue, d, cr,
                controlType is not null && AccountingCodes.ControlPartyTypes.Contains(controlType) ? controlType : null));
        }

        // Chart accounts named as parents must be able to receive sub-accounts (no entries, openings or links).
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parentCode in rows.Where(r => r.ParentCode is not null).Select(r => r.ParentCode!).Distinct())
        {
            if (existing.TryGetValue(parentCode, out var a) && (await accounts.UsesAsync(a, ct).ConfigureAwait(false)).Count > 0)
                referenced.Add(parentCode);
        }

        // Rules needing the whole file: parents, types, leaves, cycles.
        var inFile = rows.Where(r => r.Code is not null).GroupBy(r => r.Code!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var parentsInFile = rows.Where(r => r.ParentCode is not null).Select(r => r.ParentCode!).ToHashSet(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (r.ParentCode is not null)
            {
                string? parentType = null;
                if (inFile.TryGetValue(r.ParentCode, out var fileParent)) parentType = fileParent.Type;
                else if (existing.TryGetValue(r.ParentCode, out var chartParent))
                {
                    parentType = chartParent.AccountType;
                    if (chartParent.Status != AccountingCodes.Active) p.Add(r.RowNumber, r.Code, ErrorCodes.RecordArchived, r.ParentCode);
                    else if (referenced.Contains(r.ParentCode)) p.Add(r.RowNumber, r.Code, ErrorCodes.AccountParentInUse, r.ParentCode);
                }
                else p.Add(r.RowNumber, r.Code, ErrorCodes.ImportUnknown, Label("parent"), r.ParentCode);
                if (parentType is not null && r.Type is not null && parentType != r.Type)
                    p.Add(r.RowNumber, r.Code, ErrorCodes.AccountTypeMismatch, parentType);
                if (r.ParentCode == r.Code || HasCycle(r, inFile)) p.Add(r.RowNumber, r.Code, ErrorCodes.AccountCycle);
            }
            var hasChildren = r.Code is not null && parentsInFile.Contains(r.Code);
            if (hasChildren && r.Postable == true) p.Add(r.RowNumber, r.Code, ErrorCodes.AccountHasChildren, r.Code);
            if (hasChildren && (r.Debit > 0 || r.Credit > 0)) p.Add(r.RowNumber, r.Code, ErrorCodes.AccountParentInUse, r.Code);
            if (r.Postable == false && (r.Debit > 0 || r.Credit > 0)) p.Add(r.RowNumber, r.Code, ErrorCodes.AccountNotPostable, r.Code);
        }
        var preview = p.Preview(rows.Select(r => (r.RowNumber, r.Code)).ToList());
        // Parents first so navigation fix-up and inserts follow the tree.
        var ordered = rows.OrderBy(r => Depth(r, inFile)).ToList();
        return (ordered, preview);
    }

    private static bool HasCycle(Row r, Dictionary<string, Row> inFile)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var cursor = r.ParentCode; cursor is not null && inFile.TryGetValue(cursor, out var next); cursor = next.ParentCode)
        {
            if (cursor == r.Code || !visited.Add(cursor)) return true;
        }
        return false;
    }

    private static int Depth(Row r, Dictionary<string, Row> inFile)
    {
        var depth = 0;
        for (var cursor = r.ParentCode; cursor is not null && inFile.TryGetValue(cursor, out var next) && depth < 100; cursor = next.ParentCode) depth++;
        return depth;
    }

    private string Label(string field) => (ctx.Language == "en", field) switch
    {
        (true, "code") => "code",
        (false, "code") => "رمز الحساب",
        (true, "nameAr") => "Arabic name",
        (false, "nameAr") => "الاسم بالعربية",
        (true, "nameEn") => "English name",
        (false, "nameEn") => "الاسم بالإنجليزية",
        (true, "type") => "account type",
        (false, "type") => "نوع الحساب",
        (true, "postable") => "postable",
        (false, "postable") => "قابل للترحيل",
        (true, "debit") => "opening debit",
        (false, "debit") => "رصيد افتتاحي مدين",
        (true, "credit") => "opening credit",
        (false, "credit") => "رصيد افتتاحي دائن",
        (true, "control") => "control party type",
        (false, "control") => "نوع طرف الرقابة",
        (true, "parent") => "parent account",
        (false, "parent") => "الحساب الأب",
        _ => field,
    };
}
