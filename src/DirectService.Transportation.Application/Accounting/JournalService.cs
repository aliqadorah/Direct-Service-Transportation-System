using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Journal (PRD 7.5.3, 7.6.2): manual entries (acc.acct.post — create, edit = reverse + re-post per D-05, void by
/// reversal FR-113), the journal list and automatic-entries list with filters (FR-102, FR-115), entry detail with
/// links to the source document and the reversal (FR-097, FR-099), and the journal voucher (FR-103).
/// Automatic entries change only through their source document (FR-112).
/// </summary>
public sealed class JournalService(IAppDbContext db, PostingEngine engine, PartyResolver parties, PostingSources sources,
    IUrlTokenService tokens, IRecordLockGuard locks, IRequestContext ctx, ReportExporter exporter, JournalDraftService drafts)
{
    public const string EntityType = "ACC.JournalEntry";

    /// <summary>DT-279: the list filter for every cancelled entry — Reversed (its document was voided or changed, or the entry edited) or Void.</summary>
    public const string Cancelled = "Cancelled";

    // ------------------------------------------------------------ manual entries

    public async Task<JournalEntryResponse> CreateManualAsync(ManualEntryRequest r, CancellationToken ct)
    {
        var request = await BuildManualAsync(r, ct).ConfigureAwait(false);
        var entry = await db.InTransactionAsync(c => engine.PostAsync(request, c), ct).ConfigureAwait(false);
        return await GetByIdAsync(entry.Id, ct).ConfigureAwait(false);
    }

    /// <summary>D-05 / FR-112: the original is reversed (Status Reversed, linked) and the corrected entry posted with a new number.</summary>
    public async Task<JournalEntryResponse> EditManualAsync(string token, ManualEntryRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var original = await FindManualAsync(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, original.Id, ct).ConfigureAwait(false);
        var request = await BuildManualAsync(r, ct).ConfigureAwait(false);
        db.ExpectRowVersion(original, r.RowVersion);
        var entry = await db.InTransactionAsync(async c =>
        {
            var reversal = await engine.ReverseAsync(original, ReversalText(original.Number), null, c).ConfigureAwait(false);
            return await engine.PostAsync(request, "Edit", $"{original.Number} → {reversal.Number}", c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await GetByIdAsync(entry.Id, ct).ConfigureAwait(false);
    }

    /// <summary>FR-113: void by reversal with a reason; the entry keeps its number and becomes Void.</summary>
    public async Task<JournalEntryResponse> VoidManualAsync(string token, VoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var reason = TextNormalizer.CleanText(r.Reason) ?? throw Fail.Field("reason", ErrorCodes.ReasonRequired);
        if (reason.Length > 500) throw Fail.Field("reason", ErrorCodes.ValidationRange, new FieldRef("reason"));
        var original = await FindManualAsync(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, original.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(original, r.RowVersion);
        await db.InTransactionAsync(c => engine.ReverseAsync(original, VoidText(original.Number), reason, c), ct).ConfigureAwait(false);
        return await GetByIdAsync(original.Id, ct).ConfigureAwait(false);
    }

    private async Task<JournalEntry> FindManualAsync(string token, CancellationToken ct)
    {
        var publicId = tokens.Decode<JournalEntry>(token) ?? throw Fail.NotFound();
        var e = await db.Set<JournalEntry>().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        if (!e.IsManual) throw Fail.Rule(ErrorCodes.JournalNotManual);
        // A reversal belongs to the entry it reverses; voiding it would bring the original back (CR-B2-01).
        if (e.ReversesEntryId is not null) throw Fail.Rule(ErrorCodes.JournalIsReversal, e.Number);
        if (e.Status != AccountingCodes.Posted) throw Fail.Rule(ErrorCodes.JournalNotPosted, e.Number);
        return e;
    }

    /// <summary>Resolves the tokens of a manual entry; the engine applies the posting rules (FR-111, FR-106, BR-033/034).</summary>
    private async Task<PostingRequest> BuildManualAsync(ManualEntryRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var lines = r.Lines ?? [];
        new Validator()
            .Check(r.EntryDate is not null, "entryDate", ErrorCodes.ValidationRequired)
            .Require("description", r.Description)
            .MaxLength("reference", TextNormalizer.CleanText(r.Reference), PostingEngine.ReferenceMax)
            .Check(lines.Count <= 500, "lines", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        if (lines.Count < 2) throw Fail.Rule(ErrorCodes.JournalMinLines);

        var accountIds = await IdsAsync<Account>(lines.Select(l => l.AccountId).Concat(lines.Select(l => l.TaxAccountId)), ct).ConfigureAwait(false);
        var centreIds = await IdsAsync<CostCentre>(lines.Select(l => l.CostCentreId), ct).ConfigureAwait(false);
        var taxCodeIds = await IdsAsync<TaxCode>(lines.Select(l => l.TaxCodeId), ct).ConfigureAwait(false);
        var posting = new List<PostingLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var field = $"lines[{i}]";
            if (l.AccountId is null || !accountIds.TryGetValue(l.AccountId, out var accountId))
                throw Fail.Field(field + ".accountId", ErrorCodes.ValidationRequired, new FieldRef(field + ".accountId"));
            long? centreId = null;
            if (!string.IsNullOrEmpty(l.CostCentreId))
            {
                if (!centreIds.TryGetValue(l.CostCentreId, out var cc)) throw Fail.Field(field + ".costCentreId", ErrorCodes.ValidationFormat, new FieldRef(field + ".costCentreId"));
                centreId = cc;
            }
            var party = await parties.ResolveAsync(l.Party, field + ".party", ct).ConfigureAwait(false);
            // DT-255: the tax code and tax account; the engine checks them (active code, tax account for a rate above 0 %) and adds the tax line.
            long? taxCodeId = null, taxAccountId = null;
            if (!string.IsNullOrEmpty(l.TaxCodeId))
                taxCodeId = taxCodeIds.TryGetValue(l.TaxCodeId, out var tc) ? tc : throw Fail.Field(field + ".taxCodeId", ErrorCodes.ValidationFormat, new FieldRef(field + ".taxCodeId"));
            if (taxCodeId is not null && !string.IsNullOrEmpty(l.TaxAccountId))
                taxAccountId = accountIds.TryGetValue(l.TaxAccountId, out var ta) ? ta : throw Fail.Field(field + ".taxAccountId", ErrorCodes.ValidationFormat, new FieldRef(field + ".taxAccountId"));
            posting.Add(new PostingLine(null, accountId, l.Debit, l.Credit, centreId, party, l.Note, l.Reference, taxCodeId, taxAccountId));
        }
        return new PostingRequest(r.EntryDate!.Value, r.Description!, AccountingCodes.ModuleAcc, AccountingCodes.SourceManual, null, true, posting, r.Reference);
    }

    private async Task<Dictionary<string, long>> IdsAsync<T>(IEnumerable<string?> tokenList, CancellationToken ct) where T : Domain.Common.AuditableEntity
    {
        var map = new Dictionary<Guid, string>();
        foreach (var t in tokenList.Where(t => !string.IsNullOrEmpty(t)).Distinct())
        {
            if (tokens.Decode<T>(t) is Guid g) map[g] = t!;
        }
        var keys = map.Keys.ToList();
        var rows = await db.Set<T>().AsNoTracking().Where(x => keys.Contains(x.PublicId)).Select(x => new { x.PublicId, x.Id })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.ToDictionary(x => map[x.PublicId], x => x.Id, StringComparer.Ordinal);
    }

    private static string ReversalText(string number) => $"عكس القيد {number} للتعديل / Reversal of {number} (edit)";

    private static string VoidText(string number) => $"إلغاء القيد {number} / Void of {number}";

    // ------------------------------------------------------------ queries

    /// <summary>
    /// Journal list (FR-115) or, with <paramref name="automaticOnly"/>, the automatic-entries list (FR-102). Revision 6: drafts (no number,
    /// not posted) are listed under the Draft filter and, with no status chosen, before the entries (filters that drafts cannot match —
    /// number, source type, automatic — leave them out).
    /// </summary>
    public async Task<PagedResult<JournalEntrySummary>> ListAsync(JournalQuery q, bool automaticOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var page = new PageRequest(q.Page, q.PageSize);
        var withDrafts = !automaticOnly && q.Manual != false && string.IsNullOrWhiteSpace(q.Number)
            && (string.IsNullOrWhiteSpace(q.SourceType) || q.SourceType == AccountingCodes.SourceManual) && q.Status is null or AccountingCodes.Draft;
        IReadOnlyList<JournalEntrySummary> draftRows = [];
        if (withDrafts)
        {
            long? accountId = string.IsNullOrEmpty(q.AccountId) ? null
                : await db.Set<Account>().AsNoTracking().Where(a => a.PublicId == tokens.Decode<Account>(q.AccountId)).Select(a => (long?)a.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? -1;
            long? centreId = string.IsNullOrEmpty(q.CostCentreId) ? null
                : await db.Set<CostCentre>().AsNoTracking().Where(c => c.PublicId == tokens.Decode<CostCentre>(q.CostCentreId)).Select(c => (long?)c.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? -1;
            (string, long)? party = q.PartyType is not null && !string.IsNullOrEmpty(q.PartyId)
                ? (q.PartyType, await parties.IdOfAsync(q.PartyType, q.PartyId, ct).ConfigureAwait(false) ?? -1) : null;
            draftRows = await drafts.ListAsync(q, accountId, centreId, party, ct).ConfigureAwait(false);
        }
        if (q.Status == AccountingCodes.Draft)
            return new PagedResult<JournalEntrySummary>(draftRows.Skip(page.Skip).Take(page.SafePageSize).ToList(), page.SafePage, page.SafePageSize, draftRows.Count);

        var query = await FilterAsync(q, automaticOnly, ct).ConfigureAwait(false);
        var entryTotal = await query.CountAsync(ct).ConfigureAwait(false);
        // Drafts first, then the entries: the page is a slice of that sequence.
        var fromDrafts = draftRows.Skip(page.Skip).Take(page.SafePageSize).ToList();
        var entrySkip = Math.Max(0, page.Skip - draftRows.Count);
        var entryTake = page.SafePageSize - fromDrafts.Count;
        var rows = entryTake <= 0 ? [] : await query.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id)
            .Skip(entrySkip).Take(entryTake)
            .Select(e => new
            {
                e.Id, e.PublicId, e.Number, e.EntryDate, e.PeriodMonth, e.Description, e.SourceModule, e.SourceType, e.SourceId, e.IsManual, e.Status,
                e.TotalDebit, e.TotalCredit, e.Reference, e.VoidReason,
                Reverses = e.ReversesEntry == null ? null : e.ReversesEntry.Number,
                ReversedBy = e.ReversedByEntry == null ? null : e.ReversedByEntry.Number,
            })
            .ToListAsync(ct).ConfigureAwait(false);
        var reasons = await CancelReasonsAsync(rows.Select(e => new CancelRow(e.Id, e.Status, e.IsManual, e.SourceType, e.SourceId, e.VoidReason)).ToList(), ct).ConfigureAwait(false);
        var items = fromDrafts.Concat(rows.Select(e => new JournalEntrySummary(tokens.Encode<JournalEntry>(e.PublicId), e.Number, e.EntryDate, e.PeriodMonth,
            e.Description, e.SourceModule, e.SourceType, e.IsManual, e.Status, e.TotalDebit, e.TotalCredit, e.Reverses, e.ReversedBy, e.Reference,
            reasons.GetValueOrDefault(e.Id)))).ToList();
        return new PagedResult<JournalEntrySummary>(items, page.SafePage, page.SafePageSize, entryTotal + draftRows.Count);
    }

    /// <summary>The filtered entries with their lines (journal export, FR-104), oldest first.</summary>
    public async Task<IReadOnlyList<JournalEntryResponse>> ExportRowsAsync(JournalQuery q, int maxRows, CancellationToken ct)
    {
        var query = await FilterAsync(q, automaticOnly: false, ct).ConfigureAwait(false);
        var ids = await query.OrderBy(e => e.PeriodMonth).ThenBy(e => e.EntryDate).ThenBy(e => e.Id).Select(e => e.Id).Take(maxRows)
            .ToListAsync(ct).ConfigureAwait(false);
        var result = new List<JournalEntryResponse>(ids.Count);
        foreach (var chunk in ids.Chunk(1000))
            result.AddRange(await DetailsAsync(chunk, ct).ConfigureAwait(false));
        return result;
    }

    /// <summary>Excel export of the journal list with its lines (FR-115).</summary>
    /// <summary>The filtered journal line by line (FR-115): Excel (default), CSV, or a printout (PDF / HTML) through the shared report exporter.</summary>
    public async Task<GeneratedFile> ExportListAsync(JournalQuery q, string? format, CancellationToken ct)
    {
        var entries = await ExportRowsAsync(q, 20_000, ct).ConfigureAwait(false);
        var en = ctx.Language == "en";
        string L(string ar, string e) => en ? e : ar;
        ReportColumn[] columns =
        [
            new(L("الرقم", "Number")), new(L("التاريخ", "Date"), ReportValue.Date), new(L("البيان", "Description")), new(L("المرجع", "Reference")), new(L("المصدر", "Source")),
            new(L("يدوي", "Manual")), new(L("الحالة", "Status")), new(L("السطر", "Line"), ReportValue.Number), new(L("رمز الحساب", "Account code")),
            new(L("الحساب", "Account")), new(L("مدين", "Debit"), ReportValue.Money), new(L("دائن", "Credit"), ReportValue.Money), new(L("مركز الكلفة", "Cost centre")),
            new(L("الطرف", "Party")), new(L("ملاحظة", "Note")),
            new(L("مرجع السطر", "Line reference")), new(L("رمز الضريبة", "Tax code")), new(L("الضريبة", "Tax"), ReportValue.Money), new(L("سطر ضريبة آلي", "Automatic tax line")),
            new(L("الإلغاء", "Cancellation")), new(L("سبب الإلغاء", "Cancel reason")),
        ];
        var rows = entries.SelectMany(e => e.Lines.Select(l => (IReadOnlyList<object?>)
        [
            e.Number, e.EntryDate, e.Description, e.Reference, e.Source is null ? null : $"{e.Source.Type} {e.Source.Number}".Trim(), e.IsManual, e.Status,
            l.LineNo, l.Account.Code, en ? l.Account.NameEn ?? l.Account.NameAr : l.Account.NameAr, l.Debit, l.Credit, l.CostCentre?.Name,
            l.Party?.Name, l.Note,
            l.Reference, l.TaxCode?.Code, l.TaxAmount, l.TaxForLineNo is int src ? L($"للسطر {src}", $"for line {src}") : null,
            e.ReversedByEntry is { } by ? L($"أُلغي بالقيد {by.Number}", $"Cancelled by {by.Number}") : e.ReversesEntry is { } of ? L($"عكس القيد {of.Number}", $"Reversal of {of.Number}") : null,
            e.CancelReason,
        ])).ToList();
        var filters = new List<(string, string)>();
        if (q.PeriodFrom is not null || q.PeriodTo is not null) filters.Add((L("الفترة", "Period"), $"{q.PeriodFrom ?? "…"} – {q.PeriodTo ?? "…"}"));
        if (q.SourceType is not null) filters.Add((L("المصدر", "Source"), q.SourceType));
        if (q.Status is not null) filters.Add((L("الحالة", "Status"), q.Status == Cancelled ? L("ملغاة", "Cancelled") : q.Status));
        return await exporter.ExportAsync(new ReportTable(L("اليومية", "Journal"), filters, columns, rows), format, "journal", ct).ConfigureAwait(false);
    }

    private async Task<IQueryable<JournalEntry>> FilterAsync(JournalQuery q, bool automaticOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        new Validator()
            .Check(q.PeriodFrom is null || AccountingCodes.IsMonth(q.PeriodFrom), "periodFrom")
            .Check(q.PeriodTo is null || AccountingCodes.IsMonth(q.PeriodTo), "periodTo")
            .Check(q.Status is null || q.Status is AccountingCodes.Posted or AccountingCodes.Reversed or AccountingCodes.Void or AccountingCodes.Draft or Cancelled, "status")
            .Check(q.PartyType is null || PartyResolver.Types.Contains(q.PartyType), "partyType")
            .ThrowIfInvalid();
        var query = db.Set<JournalEntry>().AsNoTracking();
        if (automaticOnly) query = query.Where(e => !e.IsManual);
        else if (q.Manual is bool manual) query = query.Where(e => e.IsManual == manual);
        if (q.PeriodFrom is not null) query = query.Where(e => e.PeriodMonth.CompareTo(q.PeriodFrom) >= 0);
        if (q.PeriodTo is not null) query = query.Where(e => e.PeriodMonth.CompareTo(q.PeriodTo) <= 0);
        if (!string.IsNullOrWhiteSpace(q.Number))
        {
            var number = TextNormalizer.CleanCode(q.Number)!;
            query = query.Where(e => e.Number.Contains(number));
        }
        if (q.Status == Cancelled) query = query.Where(e => e.Status == AccountingCodes.Reversed || e.Status == AccountingCodes.Void);
        else if (q.Status is not null) query = query.Where(e => e.Status == q.Status);
        if (TextNormalizer.CleanText(q.Search) is { } search)
        {
            var code = TextNormalizer.CleanCode(search) ?? search;
            // DT-254: the entry's reference or any line reference.
            query = query.Where(e => e.Number.Contains(code) || e.Description.Contains(search) || (e.Reference != null && e.Reference.Contains(search))
                || e.Lines.Any(l => l.Reference != null && l.Reference.Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(q.SourceType)) query = query.Where(e => e.SourceType == q.SourceType);
        if (!string.IsNullOrEmpty(q.AccountId))
        {
            var publicId = tokens.Decode<Account>(q.AccountId);
            query = query.Where(e => e.Lines.Any(l => l.Account.PublicId == publicId));
        }
        if (!string.IsNullOrEmpty(q.CostCentreId))
        {
            var publicId = tokens.Decode<CostCentre>(q.CostCentreId);
            query = query.Where(e => e.Lines.Any(l => l.CostCentre != null && l.CostCentre.PublicId == publicId));
        }
        if (q.PartyType is not null && !string.IsNullOrEmpty(q.PartyId))
        {
            var partyId = await parties.IdOfAsync(q.PartyType, q.PartyId, ct).ConfigureAwait(false) ?? -1;
            var type = q.PartyType;
            query = query.Where(e => e.Lines.Any(l => l.PartyType == type && l.PartyId == partyId));
        }
        return query;
    }

    public async Task<JournalEntryResponse> GetAsync(string token, bool automaticOnly, CancellationToken ct)
    {
        var publicId = tokens.Decode<JournalEntry>(token) ?? throw Fail.NotFound();
        var id = await db.Set<JournalEntry>().AsNoTracking().Where(e => e.PublicId == publicId && (!automaticOnly || !e.IsManual))
            .Select(e => (long?)e.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        return await GetByIdAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<JournalEntryResponse> GetByIdAsync(long id, CancellationToken ct) =>
        (await DetailsAsync([id], ct).ConfigureAwait(false)).Single();

    private async Task<IReadOnlyList<JournalEntryResponse>> DetailsAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var entries = await db.Set<JournalEntry>().AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new
            {
                Entry = e,
                Reverses = e.ReversesEntry == null ? null : new { e.ReversesEntry.PublicId, e.ReversesEntry.Number },
                ReversedBy = e.ReversedByEntry == null ? null : new { e.ReversedByEntry.PublicId, e.ReversedByEntry.Number },
                BatchNo = e.ExportBatch == null ? null : e.ExportBatch.BatchNo,
                VoidedBy = e.VoidedByUser == null ? null : e.VoidedByUser.FullName,
            })
            .ToListAsync(ct).ConfigureAwait(false);
        var lines = await db.Set<EntryLine>().AsNoTracking().Where(l => ids.Contains(l.JournalEntryId))
            .Select(l => new
            {
                l.JournalEntryId, l.LineNo, l.Debit, l.Credit, l.PartyModule, l.PartyType, l.PartyId, l.Note, l.Reference, l.TaxRate, l.TaxAmount,
                Account = new { l.Account.PublicId, l.Account.Code, l.Account.NameAr, l.Account.NameEn },
                Centre = l.CostCentre == null ? null : new { l.CostCentre.PublicId, l.CostCentre.Code, l.CostCentre.Name },
                TaxCode = l.TaxCode == null ? null : new { l.TaxCode.PublicId, l.TaxCode.Code, l.TaxCode.Rate, l.TaxCode.NameAr, l.TaxCode.NameEn },
                TaxAccount = l.TaxAccount == null ? null : new { l.TaxAccount.PublicId, l.TaxAccount.Code, l.TaxAccount.NameAr, l.TaxAccount.NameEn },
                TaxForLineNo = l.TaxForLine == null ? (int?)null : l.TaxForLine.LineNo,
            })
            .ToListAsync(ct).ConfigureAwait(false);
        var creatorIds = entries.Where(e => e.Entry.CreatedBy is not null).Select(e => e.Entry.CreatedBy!.Value).Distinct().ToList();
        var creators = await db.Set<User>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(u => creatorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct).ConfigureAwait(false);
        var partyNames = await parties.DescribeAsync(lines.Select(l => (l.PartyType, l.PartyId)), ct).ConfigureAwait(false);
        var docs = await sources.DescribeAsync(entries.Select(e => (e.Entry.SourceModule, e.Entry.SourceType, e.Entry.SourceId)), ct).ConfigureAwait(false);
        var reasons = await CancelReasonsAsync(entries.Select(x => new CancelRow(x.Entry.Id, x.Entry.Status, x.Entry.IsManual, x.Entry.SourceType, x.Entry.SourceId, x.Entry.VoidReason)).ToList(), ct)
            .ConfigureAwait(false);

        var byEntry = lines.GroupBy(l => l.JournalEntryId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.LineNo).Select(l =>
            new JournalLineResponse(l.LineNo,
                new AccountRef(tokens.Encode<Account>(l.Account.PublicId), l.Account.Code, l.Account.NameAr, l.Account.NameEn),
                l.Debit, l.Credit,
                l.Centre is null ? null : new Ref(tokens.Encode<CostCentre>(l.Centre.PublicId), $"{l.Centre.Code} – {l.Centre.Name}"),
                l.PartyType is null || l.PartyId is null ? null
                    : partyNames.GetValueOrDefault((l.PartyType, l.PartyId.Value)) ?? new PartyResponse(l.PartyModule ?? string.Empty, l.PartyType, null, null),
                l.Note, l.Reference,
                l.TaxCode is null ? null : new TaxCodeRef(tokens.Encode<TaxCode>(l.TaxCode.PublicId), l.TaxCode.Code, l.TaxCode.Rate, l.TaxCode.NameAr, l.TaxCode.NameEn),
                l.TaxRate, l.TaxAmount,
                l.TaxAccount is null ? null : new AccountRef(tokens.Encode<Account>(l.TaxAccount.PublicId), l.TaxAccount.Code, l.TaxAccount.NameAr, l.TaxAccount.NameEn),
                l.TaxForLineNo)).ToList());

        var entryById = entries.ToDictionary(x => x.Entry.Id);
        return ids.Select(id => entryById[id]).Select(x =>
        {
            var e = x.Entry;
            SourceDocumentRef? source = e.SourceType is null ? null
                : e.SourceId is long sid && docs.TryGetValue((e.SourceModule, e.SourceType, sid), out var doc) ? doc
                : new SourceDocumentRef(e.SourceModule, e.SourceType, null, null);
            return new JournalEntryResponse(tokens.Encode<JournalEntry>(e.PublicId), e.Number, e.EntryDate, e.PeriodMonth, e.Description,
                e.SourceModule, e.SourceType, source, e.IsManual, e.Status, e.TotalDebit, e.TotalCredit,
                x.Reverses is null ? null : new EntryLinkRef(tokens.Encode<JournalEntry>(x.Reverses.PublicId), x.Reverses.Number),
                x.ReversedBy is null ? null : new EntryLinkRef(tokens.Encode<JournalEntry>(x.ReversedBy.PublicId), x.ReversedBy.Number),
                e.VoidReason, e.VoidedAt, x.VoidedBy, x.BatchNo, e.CreatedAt,
                e.CreatedBy is long cb ? creators.GetValueOrDefault(cb) : null,
                byEntry.GetValueOrDefault(e.Id) ?? [], Convert.ToBase64String(e.RowVersion), e.Reference, reasons.GetValueOrDefault(e.Id));
        }).ToList();
    }

    private sealed record CancelRow(long Id, string Status, bool IsManual, string? SourceType, long? SourceId, string? VoidReason);

    private sealed record DocumentVoid(long Id, string? Reason);

    /// <summary>
    /// Owner findings 27 Oct 2026 (DT-279): why each cancelled entry (Reversed / Void) was cancelled, in the reader's language — a voided manual
    /// entry: its reason; an edited manual entry: replaced by the corrected entry (D-05); the opening entry: the opening balances were saved
    /// again; an automatic entry: the void reason of its source document, or "the document was changed and posted again" (trips, advances and
    /// receipts re-post on a change).
    /// </summary>
    private async Task<Dictionary<long, string>> CancelReasonsAsync(IReadOnlyCollection<CancelRow> rows, CancellationToken ct)
    {
        var en = ctx.Language == "en";
        string L(string ar, string e) => en ? e : ar;
        var result = new Dictionary<long, string>();
        var voids = new Dictionary<(string, long), string?>();
        foreach (var g in rows.Where(r => r.Status == AccountingCodes.Reversed && !r.IsManual && r.SourceType is not null && r.SourceId is not null).GroupBy(r => r.SourceType!))
        {
            var ids = g.Select(r => r.SourceId!.Value).Distinct().ToList();
            IQueryable<DocumentVoid>? q = g.Key switch
            {
                "Trip" => db.Set<Domain.Trn.Trip>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                "Invoice" => db.Set<Domain.Trn.Invoice>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                "CreditNote" => db.Set<Domain.Trn.CreditNote>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                "Receipt" => db.Set<Domain.Trn.Receipt>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                "Advance" => db.Set<Domain.Trn.Advance>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                "Settlement" => db.Set<Domain.Trn.Settlement>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                "PartnerPayment" => db.Set<Domain.Trn.PartnerPayment>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                "SupplierBill" or "Receivable" => db.Set<ApArDocument>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                "ApArPayment" => db.Set<ApArPayment>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                "ApArReturn" => db.Set<ApArReturn>().Where(x => ids.Contains(x.Id)).Select(x => new DocumentVoid(x.Id, x.VoidReason)),
                _ => null,
            };
            if (q is null) continue;
            foreach (var d in await q.AsNoTracking().ToListAsync(ct).ConfigureAwait(false)) voids[(g.Key, d.Id)] = d.Reason;
        }
        foreach (var r in rows)
        {
            if (r.Status == AccountingCodes.Void) result[r.Id] = r.VoidReason ?? L("أُلغي القيد", "Entry voided");
            else if (r.Status != AccountingCodes.Reversed) continue;
            else if (r.IsManual) result[r.Id] = L("عُدّل القيد: حلّ محلّه قيد مصحَّح", "Entry edited: a corrected entry replaced it");
            else if (r.SourceType == AccountingCodes.SourceOpening) result[r.Id] = L("حُفظت الأرصدة الافتتاحية من جديد", "Opening balances saved again");
            else if (r.SourceType is not null && r.SourceId is long sid && voids.TryGetValue((r.SourceType, sid), out var why) && why is not null)
                result[r.Id] = L("أُلغي المستند: ", "Document voided: ") + why;
            else result[r.Id] = L("تغيّر المستند فرُحّل من جديد", "Document changed and posted again");
        }
        return result;
    }

    // ------------------------------------------------------------ voucher (FR-103, BR-024)

    public async Task<JournalVoucherResponse> VoucherAsync(string token, bool automaticOnly, CancellationToken ct)
    {
        var entry = await GetAsync(token, automaticOnly, ct).ConfigureAwait(false);
        var company = await db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId)
            .Select(c => new { c.Name, c.NameEn, c.CurrencyCode, c.CurrencyDecimals }).SingleAsync(ct).ConfigureAwait(false);
        return new JournalVoucherResponse(company.Name, company.NameEn, entry.Number, entry.EntryDate, entry.Description, entry.Status,
            entry.Lines, entry.TotalDebit, entry.TotalCredit, company.CurrencyCode,
            AmountInWords.Arabic(entry.TotalDebit, company.CurrencyCode, company.CurrencyDecimals),
            AmountInWords.English(entry.TotalDebit, company.CurrencyCode, company.CurrencyDecimals),
            CompanyProvisioningService.DefaultSignatures, entry.Reference);
    }
}
