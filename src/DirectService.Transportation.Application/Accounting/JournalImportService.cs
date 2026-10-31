using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Accounting.Domain.Acc;
using DirectService.Accounting.Domain.Cfg;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Manual journal import (acc.acct.post; FR-114). One row per line; rows with the same entry reference form one entry.
/// The preview rejects unbalanced entries, unknown or non-postable accounts, closed periods and missing parties on
/// control accounts (plus format errors). Parties are identified by their natural key (DT-27): customer name,
/// driver national ID, partner name, accounting-party name. The import is strict and posts all entries in one
/// transaction, each through the posting engine. Optional columns (owner request 25 Oct 2026): LineReference (DT-254; empty = the entry's
/// Reference), TaxCode (a tax code's code) and TaxAccount (the code of a tax account, required for a rate above 0 %) — the amounts are before
/// tax and the entry must balance with the tax (DT-255).
/// </summary>
public sealed class JournalImportService(IAppDbContext db, ISpreadsheetService sheets, IRequestContext ctx, IUrlTokenService tokens,
    PostingEngine engine, IErrorText errorText)
{
    public const string EntityType = "ACC.JournalEntry";

    private static readonly string[] RefAliases = ["EntryRef", "Entry ref", "رقم القيد المرجعي", "مرجع القيد"];
    /// <summary>The entry's free-text reference (المرجع, revision 6) — not the grouping key above.</summary>
    private static readonly string[] ReferenceAliases = ["Reference", "المرجع"];
    private static readonly string[] DateAliases = ["Date", "Entry date", "التاريخ", "تاريخ القيد"];
    private static readonly string[] DescriptionAliases = ["Description", "البيان", "الوصف"];
    private static readonly string[] AccountAliases = ["AccountCode", "Account code", "Account", "رمز الحساب", "الحساب"];
    private static readonly string[] DebitAliases = ["Debit", "مدين"];
    private static readonly string[] CreditAliases = ["Credit", "دائن"];
    private static readonly string[] CentreAliases = ["CostCentre", "Cost centre", "Cost center", "مركز الكلفة", "مركز التكلفة"];
    private static readonly string[] PartyTypeAliases = ["PartyType", "Party type", "نوع الطرف"];
    private static readonly string[] PartyAliases = ["Party", "Party key", "الطرف"];
    private static readonly string[] NoteAliases = ["Note", "Line note", "ملاحظة", "ملاحظة السطر"];
    private static readonly string[] LineReferenceAliases = ["LineReference", "Line reference", "مرجع السطر"];
    private static readonly string[] TaxCodeAliases = ["TaxCode", "Tax code", "رمز الضريبة"];
    private static readonly string[] TaxAccountAliases = ["TaxAccount", "Tax account", "TaxAccountCode", "حساب الضريبة"];

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
        long? CostCentreId, PostingParty? Party, string? Note, string? Reference = null, string? LineReference = null, long? TaxCodeId = null,
        long? TaxAccountId = null, decimal Tax = 0);

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
            note = sheet.Column(NoteAliases), referenceCol = sheet.Column(ReferenceAliases), lineReferenceCol = sheet.Column(LineReferenceAliases),
            taxCodeCol = sheet.Column(TaxCodeAliases), taxAccountCol = sheet.Column(TaxAccountAliases);
        ImportParse.RequireColumns(sheet, (refCol, RefAliases[0]), (date, DateAliases[0]), (desc, DescriptionAliases[0]),
            (account, AccountAliases[0]), (debit, DebitAliases[0]), (credit, CreditAliases[0]));

        var decimals = await engine.CurrencyDecimalsAsync(ct).ConfigureAwait(false);
        var accounts = await db.Set<Account>().AsNoTracking()
            .Select(a => new { a.Id, a.Code, a.Status, a.IsPostable, a.IsControlAccount, a.ControlPartyType, a.IsTaxAccount, HasChildren = a.Children.Any() })
            .ToDictionaryAsync(a => a.Code, StringComparer.Ordinal, ct).ConfigureAwait(false);
        var centres = await db.Set<CostCentre>().AsNoTracking().ToDictionaryAsync(c => c.Code, c => (c.Id, c.Status), StringComparer.Ordinal, ct)
            .ConfigureAwait(false);
        var closed = await db.Set<Period>().AsNoTracking().Where(p => p.Status == "Closed").Select(p => p.PeriodMonth).ToListAsync(ct).ConfigureAwait(false);
        var partyKeys = await PartyKeysAsync(ct).ConfigureAwait(false);
        var apArRoles = await db.Set<ApArParty>().AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.PartyRole, ct).ConfigureAwait(false);
        var taxCodes = await db.Set<TaxCode>().AsNoTracking().ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);

        var p = new ImportProblems(errorText, ctx.Language);
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
            if (description is null || description.Length > DirectService.Accounting.Contracts.Posting.PostingRules.DescriptionMax) p.Add(s.RowNumber, reference, ErrorCodes.ImportInvalid, Label("description"), s[desc]);
            var entryReference = referenceCol < 0 ? null : TextNormalizer.CleanText(s[referenceCol]);
            if (entryReference is { Length: > DirectService.Accounting.Contracts.Posting.PostingRules.ReferenceMax }) p.Add(s.RowNumber, reference, ErrorCodes.ImportInvalid, Label("reference"), entryReference[..20]);

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
                else if (postingParty is not null && !DirectService.Accounting.Contracts.Posting.PostingRules.PartyMatches(acc.ControlPartyType, postingParty.Module, postingParty.Type, postingParty.Id, apArRoles))
                    p.Add(s.RowNumber, reference, ErrorCodes.ControlPartyMismatch, s.RowNumber, acc.Code, acc.ControlPartyType);
            }
            var noteText = TextNormalizer.CleanText(s[note]);
            if (noteText is { Length: > DirectService.Accounting.Contracts.Posting.PostingRules.NoteMax }) p.Add(s.RowNumber, reference, ErrorCodes.ImportInvalid, Label("note"), noteText[..20]);

            // DT-254 / DT-255: line reference, tax code and tax account (labels from FieldLabels).
            var lineReference = TextNormalizer.CleanText(s[lineReferenceCol]);
            if (lineReference is { Length: > DirectService.Accounting.Contracts.Posting.PostingRules.ReferenceMax })
                p.Add(s.RowNumber, reference, ErrorCodes.ImportInvalid, new FieldRef("lineReference"), lineReference[..20]);
            long? taxCodeId = null, taxAccountId = null;
            decimal tax = 0;
            var taxCodeText = TextNormalizer.CleanCode(s[taxCodeCol]);
            if (taxCodeText is not null)
            {
                if (!taxCodes.TryGetValue(taxCodeText, out var taxCode)) p.Add(s.RowNumber, reference, ErrorCodes.ImportUnknown, new FieldRef("taxCode"), taxCodeText);
                else if (taxCode.Status != AccountingCodes.Active) p.Add(s.RowNumber, reference, ErrorCodes.TaxCodeInactive, s.RowNumber, taxCode.Code);
                else
                {
                    taxCodeId = taxCode.Id;
                    tax = JournalTax.Amount(d + cr, taxCode.Rate, decimals);
                    var taxAccountCode = TextNormalizer.CleanCode(s[taxAccountCol]);
                    var taxAcc = taxAccountCode is null ? null : accounts.GetValueOrDefault(taxAccountCode);
                    // A 0 % code needs no tax account and makes no tax line.
                    if (taxCode.Rate == 0) taxAccountId = null;
                    else if (taxAccountCode is null) p.Add(s.RowNumber, reference, ErrorCodes.TaxAccountRequired, s.RowNumber, taxCode.Code);
                    else if (taxAcc is null) p.Add(s.RowNumber, reference, ErrorCodes.ImportUnknown, new FieldRef("taxAccount"), taxAccountCode);
                    else if (!taxAcc.IsTaxAccount || taxAcc.IsControlAccount || taxAcc.Status != AccountingCodes.Active || !taxAcc.IsPostable || taxAcc.HasChildren)
                        p.Add(s.RowNumber, reference, ErrorCodes.TaxAccountInvalid, s.RowNumber, taxAcc.Code);
                    else taxAccountId = taxAcc.Id;
                }
            }
            lines.Add(new Line(s.RowNumber, reference ?? string.Empty, entryDate, description, accountId, d, cr, centreId, postingParty, noteText, entryReference,
                lineReference, taxCodeId, taxAccountId, tax));
        }

        // Entry-level rules: same date and description on every line, at least two lines, balanced (BR-033).
        var entries = new List<PostingRequest>();
        foreach (var group in lines.Where(l => l.Ref.Length > 0).GroupBy(l => l.Ref, StringComparer.Ordinal))
        {
            var first = group.First();
            foreach (var l in group.Skip(1).Where(l => l.Date != first.Date || l.Description != first.Description || (l.Reference is not null && l.Reference != first.Reference)))
                p.Add(l.Row, l.Ref, ErrorCodes.ImportInvalid, Label("sameEntry"), l.Ref);
            if (group.Count() < 2) p.Add(first.Row, first.Ref, ErrorCodes.JournalMinLines);
            // Balanced including the tax, which is added on the same side as its line (DT-255).
            var taxDr = group.Where(l => l.Debit > 0).Sum(l => l.Tax);
            var taxCr = group.Where(l => l.Debit == 0).Sum(l => l.Tax);
            var dr = group.Sum(l => l.Debit) + taxDr;
            var crTotal = group.Sum(l => l.Credit) + taxCr;
            if (dr != crTotal)
            {
                foreach (var l in group)
                {
                    if (taxDr + taxCr > 0) p.Add(l.Row, l.Ref, ErrorCodes.JournalUnbalancedWithTax, Money.Format(dr), Money.Format(taxDr), Money.Format(crTotal), Money.Format(taxCr));
                    else p.Add(l.Row, l.Ref, ErrorCodes.JournalUnbalanced, Money.Format(dr), Money.Format(crTotal));
                }
            }
            if (first.Date is DateOnly entryDate && first.Description is not null)
            {
                entries.Add(new PostingRequest(entryDate, first.Description, AccountingCodes.ModuleAcc, AccountingCodes.SourceManual, null, true,
                    group.Select(l => new PostingLine(null, l.AccountId, l.Debit, l.Credit, l.CostCentreId, l.Party, l.Note, l.LineReference, l.TaxCodeId, l.TaxAccountId)).ToList(),
                    first.Reference));
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
        (true, "reference") => "reference",
        (false, "reference") => "المرجع",
        (true, "sameEntry") => "date/description/reference (must match the entry's first line)",
        (false, "sameEntry") => "التاريخ/البيان/المرجع (يجب أن يطابق السطر الأول من القيد)",
        _ => field,
    };
}
