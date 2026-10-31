using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Accounting.Domain.Acc;
using DirectService.Accounting.Domain.Cfg;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Budgets;

/// <summary>
/// Budget Excel import (revision 6 part B), like the other importers: a template in the user's language (account code, cost centre code, the
/// twelve months, a notes sheet), a strict preview (every problem with its row, column and fix) and the import, which replaces all lines of a
/// draft budget in one transaction and is logged in cfg.ImportBatches. Month columns are matched by English or Arabic month names (both the
/// Levantine and the Egyptian forms) or by number (1–12, M01–M12).
/// </summary>
public sealed class BudgetImportService(IAppDbContext db, IRequestContext ctx, ISpreadsheetService sheets, MasterDataSupport support, DirectService.Accounting.Contracts.Posting.IAccountingPosting posting,
    BudgetService budgets, IUrlTokenService tokens, IErrorText errorText)
{
    private static readonly string[] AccountAliases = ["Account code", "AccountCode", "Account", "رمز الحساب", "الحساب"];
    private static readonly string[] CentreAliases = ["Cost centre", "Cost center", "CostCentre", "Cost centre code", "مركز التكلفة", "مركز الكلفة", "رمز مركز التكلفة"];

    /// <summary>Month names: English, Arabic (Levantine, as in the template), Arabic (Egyptian).</summary>
    public static readonly IReadOnlyList<(string En, string Ar, string ArAlt)> MonthNames =
    [
        ("January", "كانون الثاني", "يناير"), ("February", "شباط", "فبراير"), ("March", "آذار", "مارس"), ("April", "نيسان", "أبريل"),
        ("May", "أيار", "مايو"), ("June", "حزيران", "يونيو"), ("July", "تموز", "يوليو"), ("August", "آب", "أغسطس"),
        ("September", "أيلول", "سبتمبر"), ("October", "تشرين الأول", "أكتوبر"), ("November", "تشرين الثاني", "نوفمبر"), ("December", "كانون الأول", "ديسمبر"),
    ];

    private bool En => ctx.Language == "en";

    private string T(string ar, string en) => En ? en : ar;

    private static string[] MonthAliases(int m)
    {
        var (en, ar, alt) = MonthNames[m];
        var n = m + 1;
        return [en, en[..3], ar, alt, n.ToString(CultureInfo.InvariantCulture), $"M{n:00}", $"Month {n}", $"شهر {n}"];
    }

    // ------------------------------------------------------------ template

    /// <summary>The import template of a budget of this company; another company's or tenant's budget is not found (DT-218).</summary>
    public async Task<GeneratedFile> TemplateAsync(string budgetToken, CancellationToken ct)
    {
        _ = await support.FindAsync<Budget>(budgetToken, ct).ConfigureAwait(false);
        async Task<string> CodeOf(string type, string fallback) =>
            await db.Set<Account>().AsNoTracking().Where(a => a.AccountType == type && a.IsPostable && a.Status == AccountingCodes.Active && !a.Children.Any())
                .OrderBy(a => a.Code).Select(a => a.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? fallback;
        var revenue = await CodeOf(BudgetService.Revenue, "4101").ConfigureAwait(false);
        var expense = await CodeOf(BudgetService.Expense, "5101").ConfigureAwait(false);
        var monthHeaders = Enumerable.Range(0, 12).Select(m => En ? MonthNames[m].En : MonthNames[m].Ar).ToList();
        var headers = new List<string> { T("رمز الحساب", "Account code"), T("مركز التكلفة", "Cost centre") };
        headers.AddRange(monthHeaders);
        var required = new List<bool> { true, false };
        required.AddRange(Enumerable.Repeat(false, 12));
        IReadOnlyList<object?> Sample(string code, decimal amount) => [code, null, .. Enumerable.Repeat<object?>(amount, 12)];
        var notes = new List<IReadOnlyList<string>>
        {
            new[] { headers[0], T("نعم", "Yes"), T("رمز حساب في الدليل", "Code of an account in the chart"),
                T("أي حساب في الدليل؛ حساب فرعي أو حساب أب (لا تضع الأب وفروعه معاً لمركز التكلفة نفسه). الصفوف التي كل مبالغها صفر لا تُحفظ", "Any account of the chart; a posting account or a parent (not a parent and its sub-accounts for the same cost centre). Rows whose amounts are all zero are not kept") },
            new[] { headers[1], T("لا", "No"), T("رمز مركز تكلفة", "Code of a cost centre"), T("فارغ = بلا مركز تكلفة؛ الحساب مرة واحدة لكل مركز", "Empty = no cost centre; an account once per cost centre") },
        };
        notes.AddRange(monthHeaders.Select(h => (IReadOnlyList<string>)[h, T("لا", "No"), T("رقم، حتى 3 خانات عشرية؛ يمكن أن يكون سالباً", "Number, up to 3 decimals; may be negative"),
            T("فارغ = صفر. الإيرادات والمصروفات بقيمتها الموجبة", "Empty = zero. Revenue and expenses as positive amounts")]));
        var template = new SpreadsheetTemplate(T("الموازنة", "Budget"), T("شرح الأعمدة", "Column notes"), !En, headers, required,
            [Sample(revenue, 10000m), Sample(expense, 2500m)],
            T("الصف الأول عناوين الأعمدة كما يقرؤها الاستيراد: لا تغيّرها. الاستيراد يستبدل كل أسطر الموازنة (مسودة) بأسطر الملف. استبدل الصفين المثالين بأسطرك.",
                "The first row holds the column headers the import reads: do not change them. The import replaces every line of the (draft) budget with the file's lines. Replace the two sample rows with your lines."),
            [T("العمود", "Column"), T("مطلوب", "Required"), T("الصيغة", "Format"), T("القيم المسموحة / ملاحظات", "Allowed values / notes")], notes);
        return new GeneratedFile($"budget-template-{(En ? "en" : "ar")}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sheets.WriteTemplate(template));
    }

    // ------------------------------------------------------------ preview / import

    private sealed record Row(int RowNumber, long AccountId, long? CostCentreId, decimal[] Months);

    public async Task<ImportPreview> PreviewAsync(string budgetToken, Stream content, string fileName, long length, CancellationToken ct)
    {
        var budget = await support.FindAsync<Budget>(budgetToken, ct).ConfigureAwait(false);
        BudgetService.EnsureDraft(budget);
        var (_, preview) = await AnalyseAsync(content, fileName, length, ct).ConfigureAwait(false);
        return preview;
    }

    public async Task<ImportResult> ImportAsync(string budgetToken, Stream content, string fileName, long length, CancellationToken ct)
    {
        var budget = await support.FindAsync<Budget>(budgetToken, ct).ConfigureAwait(false);
        BudgetService.EnsureDraft(budget);
        var (rows, preview) = await AnalyseAsync(content, fileName, length, ct).ConfigureAwait(false);
        if (preview.RejectedRows > 0) throw Fail.Rule(ErrorCodes.ImportHasErrors, preview.RejectedRows);
        var batch = await db.InTransactionAsync(async c =>
        {
            var changed = await budgets.ReplaceLinesAsync(budget, rows.Where(r => r.Months.Any(m => m != 0)).Select(r => (r.AccountId, r.CostCentreId, r.Months)).ToList(), "Replaced by an import", c).ConfigureAwait(false);
            if (changed) db.Set<Budget>().Entry(budget).Property(b => b.UpdatedAt).IsModified = true;
            var b = ImportParse.Batch(BudgetService.EntityType, fileName, rows.Count, rows.Count, ctx.UserId!.Value);
            db.Set<ImportBatch>().Add(b);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return b;
        }, ct).ConfigureAwait(false);
        return new ImportResult(tokens.Encode<ImportBatch>(batch.PublicId), rows.Count, rows.Count);
    }

    private async Task<(List<Row> Rows, ImportPreview Preview)> AnalyseAsync(Stream content, string fileName, long length, CancellationToken ct)
    {
        var sheet = sheets.Read(content, fileName, length);
        int account = sheet.Column(AccountAliases), centre = sheet.Column(CentreAliases);
        var months = Enumerable.Range(0, 12).Select(m => sheet.Column(MonthAliases(m))).ToArray();
        ImportParse.RequireColumns(sheet, (account, AccountAliases[0]));
        if (months.All(m => m < 0)) throw Fail.Rule(ErrorCodes.ImportMissingColumn, MonthNames[0].En);

        var decimals = await posting.CurrencyDecimalsAsync(ct).ConfigureAwait(false);
        var accounts = await db.Set<Account>().AsNoTracking().Select(a => new { a.Id, a.Code, a.AccountType, a.ParentId }).ToListAsync(ct).ConfigureAwait(false);
        var byCode = accounts.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        var parentOf = accounts.ToDictionary(a => a.Id, a => a.ParentId);
        var codeOf = accounts.ToDictionary(a => a.Id, a => a.Code);
        var centres = await db.Set<CostCentre>().AsNoTracking().Select(c => new { c.Id, c.Code }).ToListAsync(ct).ConfigureAwait(false);
        var centreByCode = centres.ToDictionary(c => c.Code, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var p = new ImportProblems(errorText, ctx.Language);
        var rows = new List<Row>();
        var keys = new List<(int Row, string? Key)>();
        foreach (var s in sheet.Rows)
        {
            var code = TextNormalizer.CleanCode(s[account]);
            keys.Add((s.RowNumber, code));
            long accountId = 0;
            if (code is null || !byCode.TryGetValue(code, out var a)) p.AddAt(s.RowNumber, code, sheet.Headers[account], ErrorCodes.ImportUnknown, null, sheet.Headers[account], s[account]);
            else if (!BudgetService.IsBudgetType(a.AccountType)) p.AddAt(s.RowNumber, code, sheet.Headers[account], ErrorCodes.BudgetAccountType, null, a.Code);
            else accountId = a.Id;
            long? centreId = null;
            var centreCode = centre < 0 ? null : TextNormalizer.CleanCode(s[centre]);
            if (centreCode is not null)
            {
                if (centreByCode.TryGetValue(centreCode, out var cid)) centreId = cid;
                else p.AddAt(s.RowNumber, code, sheet.Headers[centre], ErrorCodes.ImportUnknown, null, sheet.Headers[centre], centreCode);
            }
            var values = new decimal[12];
            for (var m = 0; m < 12; m++)
            {
                if (months[m] < 0) continue;
                if (!TryAmount(s[months[m]], decimals, out values[m])) p.AddAt(s.RowNumber, code, sheet.Headers[months[m]], ErrorCodes.ImportInvalid, null, sheet.Headers[months[m]], s[months[m]]);
            }
            if (accountId != 0) rows.Add(new Row(s.RowNumber, accountId, centreId, values));
        }
        // Duplicates and parent/sub-account overlaps between the file's rows.
        var valid = rows.Where(r => !p.Has(r.RowNumber)).ToList();
        while (BudgetMath.FindConflict(valid.Select(r => (r.AccountId, r.CostCentreId)).ToList(), id => parentOf.GetValueOrDefault(id)) is { } conflict)
        {
            var r = valid[conflict.Index];
            if (conflict.AncestorId is long anc) p.AddAt(r.RowNumber, codeOf[r.AccountId], sheet.Headers[account], ErrorCodes.BudgetOverlap, null, codeOf[r.AccountId], codeOf[anc]);
            else p.AddAt(r.RowNumber, codeOf[r.AccountId], sheet.Headers[account], ErrorCodes.ImportDuplicate, null, codeOf[r.AccountId]);
            valid.RemoveAt(conflict.Index);
        }
        return (rows, p.Preview(keys));
    }

    /// <summary>An amount cell: empty = 0; a leading minus sign (or parentheses) for negative amounts; otherwise as the other importers.</summary>
    private static bool TryAmount(string? text, int decimals, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = TextNormalizer.ToAsciiDigits(text).Trim();
        var negative = t.StartsWith('-') || t.StartsWith('−') || (t.StartsWith('(') && t.EndsWith(')'));
        t = t.TrimStart('-', '−', '(').TrimEnd(')');
        if (!ImportParse.TryAmount(t, decimals, out var v)) return false;
        value = negative ? -v : v;
        return true;
    }
}
