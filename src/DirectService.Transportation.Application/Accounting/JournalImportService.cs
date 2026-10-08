using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Manual journal import (acc.acct.post; FR-114). One row per line; rows with the same entry reference form one entry.
/// The preview rejects unbalanced entries, unknown or non-postable accounts, closed periods and missing parties on
/// control accounts (plus format errors). Parties are identified by their natural key (DT-27): customer name,
/// driver national ID, partner name, accounting-party name. The import is strict and posts all entries in one
/// transaction, each through the posting engine.
/// </summary>
public sealed class JournalImportService(IAppDbContext db, ISpreadsheetService sheets, IRequestContext ctx, IUrlTokenService tokens,
    PostingEngine engine)
{
    public const string EntityType = "ACC.JournalEntry";

    private static readonly string[] RefAliases = ["EntryRef", "Entry ref", "Reference", "رقم القيد المرجعي", "مرجع القيد"];
    private static readonly string[] DateAliases = ["Date", "Entry date", "التاريخ", "تاريخ القيد"];
    private static readonly string[] DescriptionAliases = ["Description", "البيان", "الوصف"];
    private static readonly string[] AccountAliases = ["AccountCode", "Account code", "Account", "رمز الحساب", "الحساب"];
    private static readonly string[] DebitAliases = ["Debit", "مدين"];
    private static readonly string[] CreditAliases = ["Credit", "دائن"];
    private static readonly string[] CentreAliases = ["CostCentre", "Cost centre", "Cost center", "مركز الكلفة", "مركز التكلفة"];
    private static readonly string[] PartyTypeAliases = ["PartyType", "Party type", "نوع الطرف"];
    private static readonly string[] PartyAliases = ["Party", "Party key", "الطرف"];
    private static readonly string[] NoteAliases = ["Note", "Line note", "ملاحظة", "ملاحظة السطر"];

    private static readonly Dictionary<string, string> PartyTypeNames = new(StringComparer.Ordinal)
    {
        [TextNormalizer.Key("Customer")] = AccountingCodes.PartyCustomer, [TextNormalizer.Key("عميل")] = AccountingCodes.PartyCustomer,
        [TextNormalizer.Key("Driver")] = AccountingCodes.PartyDriver, [TextNormalizer.Key("سائق")] = AccountingCodes.PartyDriver,
        [TextNormalizer.Key("Partner")] = AccountingCodes.PartyPartner, [TextNormalizer.Key("شريك")] = AccountingCodes.PartyPartner,
        [TextNormalizer.Key("ApArParty")] = AccountingCodes.PartyApAr, [TextNormalizer.Key("طرف محاسبي")] = AccountingCodes.PartyApAr,
        [TextNormalizer.Key("Supplier")] = SupplierKey, [TextNormalizer.Key("مورد")] = SupplierKey,
    };

    /// <summary>Import party type naming an accounting party with the Supplier role.</summary>
    private const string SupplierKey = AccountingCodes.PartyApAr + ":Supplier";

    /// <summary>Marks a natural key shared by several parties: such rows are rejected, never guessed (CR-B2-05).</summary>
    private const long Ambiguous = -1;

    private sealed record Line(int Row, string Ref, DateOnly? Date, string? Description, long? AccountId, decimal Debit, decimal Credit,
        long? CostCentreId, PostingParty? Party, string? Note);

    public async Task<ImportPreview> PreviewAsync(Stream content, string fileName, long length, CancellationToken ct) =>
        (await AnalyseAsync(content, fileName, length, ct).ConfigureAwait(false)).Preview;

    public async Task<ImportResult> ImportAsync(Stream content, string fileName, long length, CancellationToken ct)
    {
        var (entries, preview) = await AnalyseAsync(content, fileName, length, ct).ConfigureAwait(false);
        if (preview.RejectedRows > 0) throw Fail.Rule(ErrorCodes.ImportHasErrors, preview.RejectedRows);
        var batch = await db.InTransactionAsync(async c =>
        {
            foreach (var e in entries)
                await engine.PostAsync(e, "Import", fileName.Length <= 200 ? fileName : fileName[^200..], c).ConfigureAwait(false);
            var b = ImportParse.Batch(EntityType, fileName, preview.TotalRows, preview.TotalRows, ctx.UserId!.Value);
            db.Set<ImportBatch>().Add(b);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return b;
        }, ct).ConfigureAwait(false);
        return new ImportResult(tokens.Encode<ImportBatch>(batch.PublicId), preview.TotalRows, preview.TotalRows);
    }

    private async Task<(List<PostingRequest> Entries, ImportPreview Preview)> AnalyseAsync(Stream content, string fileName, long length, CancellationToken ct)
    {
        var sheet = sheets.Read(content, fileName, length);
        int refCol = sheet.Column(RefAliases), date = sheet.Column(DateAliases), desc = sheet.Column(DescriptionAliases),
            account = sheet.Column(AccountAliases), debit = sheet.Column(DebitAliases), credit = sheet.Column(CreditAliases),
            centre = sheet.Column(CentreAliases), partyType = sheet.Column(PartyTypeAliases), party = sheet.Column(PartyAliases),
            note = sheet.Column(NoteAliases);
        ImportParse.RequireColumns(sheet, (refCol, RefAliases[0]), (date, DateAliases[0]), (desc, DescriptionAliases[0]),
            (account, AccountAliases[0]), (debit, DebitAliases[0]), (credit, CreditAliases[0]));

        var decimals = await engine.CurrencyDecimalsAsync(ct).ConfigureAwait(false);
        var accounts = await db.Set<Account>().AsNoTracking()
            .Select(a => new { a.Id, a.Code, a.Status, a.IsPostable, a.IsControlAccount, a.ControlPartyType, HasChildren = a.Children.Any() })
            .ToDictionaryAsync(a => a.Code, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var centres = await db.Set<CostCentre>().AsNoTracking().ToDictionaryAsync(c => c.Code, c => (c.Id, c.Status), StringComparer.Ordinal, ct)
            .ConfigureAwait(false);
        var closed = await db.Set<Period>().AsNoTracking().Where(p => p.Status == "Closed").Select(p => p.PeriodMonth).ToListAsync(ct).ConfigureAwait(false);
        var partyKeys = await PartyKeysAsync(ct).ConfigureAwait(false);
        var apArRoles = await db.Set<ApArParty>().AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.PartyRole, ct).ConfigureAwait(false);

        var p = new ImportProblems(ctx.Language);
        var lines = new List<Line>();
        foreach (var s in sheet.Rows)
        {
            var reference = TextNormalizer.CleanCode(s[refCol]);
            if (reference is null) p.Add(s.RowNumber, null, ErrorCodes.ImportInvalid, Label("ref"), s[refCol]);
            DateOnly? entryDate = ImportParse.TryDate(s[date], out var dt) ? dt : null;
            if (entryDate is null) p.Add(s.RowNumber, reference, ErrorCodes.ImportInvalid, Label("date"), s[date]);
            else if (closed.Contains(AccountingCodes.Month(dt)))
                p.Add(s.RowNumber, reference, ErrorCodes.PeriodClosed, dt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), AccountingCodes.Month(dt));
            var description = TextNormalizer.CleanText(s[desc]);
            if (description is null || description.Length > PostingEngine.DescriptionMax) p.Add(s.RowNumber, reference, ErrorCodes.ImportInvalid, Label("description"), s[desc]);

            long? accountId = null;
            var code = TextNormalizer.CleanCode(s[account]);
            var acc = code is null ? null : accounts.GetValueOrDefault(code);
            if (acc is null) p.Add(s.RowNumber, reference, ErrorCodes.ImportUnknown, Label("account"), s[account]);
            else if (acc.Status != AccountingCodes.Active || !acc.IsPostable || acc.HasChildren) p.Add(s.RowNumber, reference, ErrorCodes.AccountNotPostable, acc.Code);
            else accountId = acc.Id;

            var amountsOk = ImportParse.TryAmount(s[debit], decimals, out var d) & ImportParse.TryAmount(s[credit], decimals, out var cr);
            if (!amountsOk || (d > 0) == (cr > 0)) p.Add(s.RowNumber, reference, ErrorCodes.JournalLineAmount, s.RowNumber);

            long? centreId = null;
            var centreCode = TextNormalizer.CleanCode(s[centre]);
            if (centreCode is not null)
            {
                if (!centres.TryGetValue(centreCode, out var cc)) p.Add(s.RowNumber, reference, ErrorCodes.ImportUnknown, Label("centre"), centreCode);
                else if (cc.Status != AccountingCodes.Active) p.Add(s.RowNumber, reference, ErrorCodes.RecordArchived, centreCode);
                else centreId = cc.Id;
            }

            PostingParty? postingParty = null;
            var typeText = s[partyType];
            var partyText = s[party];
            if (!string.IsNullOrWhiteSpace(typeText) || !string.IsNullOrWhiteSpace(partyText))
            {
                var type = typeText is null ? null : PartyTypeNames.GetValueOrDefault(TextNormalizer.Key(typeText));
                if (type is null) p.Add(s.RowNumber, reference, ErrorCodes.ImportInvalid, Label("partyType"), typeText);
                else if (partyKeys.TryGetValue((type, TextNormalizer.Key(partyText)), out var partyId) && partyId == Ambiguous)
                    p.Add(s.RowNumber, reference, ErrorCodes.ImportInvalid, Label("party"), partyText + " (×2)");
                else if (partyKeys.TryGetValue((type, TextNormalizer.Key(partyText)), out partyId))
                {
                    var resolvedType = type == SupplierKey ? AccountingCodes.PartyApAr : type;
                    postingParty = new PostingParty(PartyResolver.ModuleOf(resolvedType), resolvedType, partyId);
                }
                else p.Add(s.RowNumber, reference, ErrorCodes.ImportUnknown, Label("party"), partyText);
            }
            if (acc is { IsControlAccount: true })
            {
                if (postingParty is null && !p.Has(s.RowNumber)) p.Add(s.RowNumber, reference, ErrorCodes.ControlPartyRequired, s.RowNumber, acc.Code, acc.ControlPartyType);
                else if (postingParty is not null && !PostingEngine.PartyMatches(acc.ControlPartyType, postingParty, apArRoles))
                    p.Add(s.RowNumber, reference, ErrorCodes.ControlPartyMismatch, s.RowNumber, acc.Code, acc.ControlPartyType);
            }
            var noteText = TextNormalizer.CleanText(s[note]);
            if (noteText is { Length: > PostingEngine.NoteMax }) p.Add(s.RowNumber, reference, ErrorCodes.ImportInvalid, Label("note"), noteText[..20]);
            lines.Add(new Line(s.RowNumber, reference ?? string.Empty, entryDate, description, accountId, d, cr, centreId, postingParty, noteText));
        }

        // Entry-level rules: same date and description on every line, at least two lines, balanced (BR-033).
        var entries = new List<PostingRequest>();
        foreach (var group in lines.Where(l => l.Ref.Length > 0).GroupBy(l => l.Ref, StringComparer.Ordinal))
        {
            var first = group.First();
            foreach (var l in group.Skip(1).Where(l => l.Date != first.Date || l.Description != first.Description))
                p.Add(l.Row, l.Ref, ErrorCodes.ImportInvalid, Label("sameEntry"), l.Ref);
            if (group.Count() < 2) p.Add(first.Row, first.Ref, ErrorCodes.JournalMinLines);
            var dr = group.Sum(l => l.Debit);
            var crTotal = group.Sum(l => l.Credit);
            if (dr != crTotal)
            {
                foreach (var l in group) p.Add(l.Row, l.Ref, ErrorCodes.JournalUnbalanced, Money.Format(dr), Money.Format(crTotal));
            }
            if (first.Date is DateOnly entryDate && first.Description is not null)
            {
                entries.Add(new PostingRequest(entryDate, first.Description, AccountingCodes.ModuleAcc, AccountingCodes.SourceManual, null, true,
                    group.Select(l => new PostingLine(null, l.AccountId, l.Debit, l.Credit, l.CostCentreId, l.Party, l.Note)).ToList()));
            }
        }
        var preview = p.Preview(lines.Select(l => (l.Row, (string?)l.Ref)).ToList());
        return (entries, preview);
    }

    /// <summary>Party lookup by natural key (DT-27), keys normalised like other uniqueness checks (case, spaces, Arabic digits).</summary>
    private async Task<Dictionary<(string Type, string Key), long>> PartyKeysAsync(CancellationToken ct)
    {
        var map = new Dictionary<(string, string), long>();
        void Put(string type, string key, long id) => map[(type, key)] = map.TryGetValue((type, key), out var existing) && existing != id ? Ambiguous : id;
        foreach (var c in await db.Set<Customer>().AsNoTracking().Where(x => x.Status != AccountingCodes.Archived).Select(x => new { x.Id, x.Name })
                     .ToListAsync(ct).ConfigureAwait(false))
            Put(AccountingCodes.PartyCustomer, TextNormalizer.Key(c.Name), c.Id);
        foreach (var d in await db.Set<Driver>().AsNoTracking().Where(x => x.Status != AccountingCodes.Archived).Select(x => new { x.Id, x.NationalId })
                     .ToListAsync(ct).ConfigureAwait(false))
            Put(AccountingCodes.PartyDriver, TextNormalizer.Key(d.NationalId), d.Id);
        foreach (var x in await db.Set<Partner>().AsNoTracking().Where(x => x.Status != AccountingCodes.Archived).Select(x => new { x.Id, x.Name })
                     .ToListAsync(ct).ConfigureAwait(false))
            Put(AccountingCodes.PartyPartner, TextNormalizer.Key(x.Name), x.Id);
        foreach (var x in await db.Set<ApArParty>().AsNoTracking().Where(x => x.Status != AccountingCodes.Archived).Select(x => new { x.Id, x.Name, x.PartyRole })
                     .ToListAsync(ct).ConfigureAwait(false))
        {
            Put(AccountingCodes.PartyApAr, TextNormalizer.Key(x.Name), x.Id);
            if (x.PartyRole == "Supplier") Put(SupplierKey, TextNormalizer.Key(x.Name), x.Id);
        }
        return map;
    }

    private string Label(string field) => (ctx.Language == "en", field) switch
    {
        (true, "ref") => "entry reference",
        (false, "ref") => "رقم القيد المرجعي",
        (true, "date") => "date",
        (false, "date") => "التاريخ",
        (true, "description") => "description",
        (false, "description") => "البيان",
        (true, "account") => "account",
        (false, "account") => "الحساب",
        (true, "centre") => "cost centre",
        (false, "centre") => "مركز الكلفة",
        (true, "partyType") => "party type",
        (false, "partyType") => "نوع الطرف",
        (true, "party") => "party",
        (false, "party") => "الطرف",
        (true, "note") => "note",
        (false, "note") => "الملاحظة",
        (true, "sameEntry") => "date/description (must match the entry's first line)",
        (false, "sameEntry") => "التاريخ/البيان (يجب أن يطابق السطر الأول من القيد)",
        _ => field,
    };
}
