using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Journal entry drafts (revision 6, owner change request 15 Oct 2026): "Save as draft" on the new journal entry screen. A draft has no JV
/// number, does not post and is never part of a balance; it is listed in the journal (Draft filter, first when no status is chosen) and can
/// be edited, deleted or posted. Posting validates it like a new manual entry and posts it through the posting engine, so the JV number is
/// taken only then (gap-free numbering unchanged) and the period lock is checked then; the draft is removed in the same transaction.
/// </summary>
public sealed class JournalDraftService(IAppDbContext db, PostingEngine engine, PartyResolver parties, IUrlTokenService tokens, IChangeContext changes)
{
    public const string EntityType = "ACC.JournalDraft";

    /// <summary>The most drafts read for the journal list (drafts are few: work in progress).</summary>
    public const int ListMax = 1000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A stored draft line: internal ids, so a draft survives a change of the URL-token key.</summary>
    private sealed record DraftLine(long? AccountId, decimal Debit, decimal Credit, long? CostCentreId, string? PartyType, long? PartyId, string? Note);

    public async Task<JournalDraftResponse> CreateAsync(ManualEntryRequest r, CancellationToken ct)
    {
        var draft = new JournalDraft();
        await FillAsync(draft, r, ct).ConfigureAwait(false);
        db.Set<JournalDraft>().Add(draft);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(tokens.Encode<JournalDraft>(draft.PublicId), ct).ConfigureAwait(false);
    }

    public async Task<JournalDraftResponse> UpdateAsync(string token, ManualEntryRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var draft = await FindAsync(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(draft, r.RowVersion);
        await FillAsync(draft, r, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(token, ct).ConfigureAwait(false);
    }

    /// <summary>Deletes a draft (soft delete; nothing was posted, no number was taken).</summary>
    public async Task DeleteAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var draft = await FindAsync(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(draft, rowVersion);
        draft.DeleteReason = "Draft deleted";
        db.Set<JournalDraft>().Remove(draft);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts a draft: full manual-entry validation (date, description, at least two lines, accounts, parties, balance — FR-111, FR-106,
    /// BR-033/034), period lock and JV number at this moment; the draft is removed with the same transaction. Returns the posted entry id.
    /// </summary>
    public async Task<long> PostAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var draft = await FindAsync(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(draft, rowVersion);
        var lines = Read(draft.Lines);
        new Validator()
            .Check(draft.EntryDate is not null, "entryDate", ErrorCodes.ValidationRequired)
            .Require("description", draft.Description)
            .ThrowIfInvalid();
        if (lines.Count < 2) throw Fail.Rule(ErrorCodes.JournalMinLines);
        var posting = new List<PostingLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.AccountId is null) throw Fail.Field($"lines[{i}].accountId", ErrorCodes.ValidationRequired, new FieldRef($"lines[{i}].accountId"));
            var party = l.PartyType is not null && l.PartyId is long pid ? new PostingParty(PartyResolver.ModuleOf(l.PartyType), l.PartyType, pid) : null;
            posting.Add(new PostingLine(null, l.AccountId, l.Debit, l.Credit, l.CostCentreId, party, l.Note));
        }
        var request = new PostingRequest(draft.EntryDate!.Value, draft.Description!, AccountingCodes.ModuleAcc, AccountingCodes.SourceManual, null, true,
            posting, draft.Reference);
        var entry = await db.InTransactionAsync(async c =>
        {
            var posted = await engine.PostAsync(request, c).ConfigureAwait(false);
            draft.DeleteReason = "Posted as " + posted.Number;
            changes.SetAction(draft, "Post", posted.Number);
            db.Set<JournalDraft>().Remove(draft);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return posted;
        }, ct).ConfigureAwait(false);
        return entry.Id;
    }

    public async Task<JournalDraftResponse> GetAsync(string token, CancellationToken ct)
    {
        var publicId = tokens.Decode<JournalDraft>(token) ?? throw Fail.NotFound();
        var d = await db.Set<JournalDraft>().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        var lines = Read(d.Lines);
        var accountIds = lines.Where(l => l.AccountId is not null).Select(l => l.AccountId!.Value).Distinct().ToList();
        var accounts = await db.Set<Account>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new AccountRef(tokens.Encode<Account>(a.PublicId), a.Code, a.NameAr, a.NameEn), ct).ConfigureAwait(false);
        var centreIds = lines.Where(l => l.CostCentreId is not null).Select(l => l.CostCentreId!.Value).Distinct().ToList();
        var centres = await db.Set<CostCentre>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(c => centreIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new Ref(tokens.Encode<CostCentre>(c.PublicId), $"{c.Code} – {c.Name}"), ct).ConfigureAwait(false);
        var partyNames = await parties.DescribeAsync(lines.Select(l => (l.PartyType, l.PartyId)), ct).ConfigureAwait(false);
        var creator = d.CreatedBy is long cb
            ? await db.Set<User>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(u => u.Id == cb).Select(u => u.FullName).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        return new JournalDraftResponse(token, d.EntryDate, d.Description, d.Reference,
            lines.Select(l => new JournalDraftLineResponse(
                l.AccountId is long a ? accounts.GetValueOrDefault(a) : null, l.Debit, l.Credit,
                l.CostCentreId is long c ? centres.GetValueOrDefault(c) : null,
                l.PartyType is not null && l.PartyId is long p ? partyNames.GetValueOrDefault((l.PartyType, p)) : null,
                l.Note)).ToList(),
            d.TotalDebit, d.TotalCredit, d.CreatedAt, creator, Convert.ToBase64String(d.RowVersion));
    }

    /// <summary>The company's drafts for the journal list, newest first, filtered like the list (period, search, account, cost centre, party).</summary>
    public async Task<IReadOnlyList<JournalEntrySummary>> ListAsync(JournalQuery q, long? accountId, long? costCentreId, (string Type, long Id)? party, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var rows = await db.Set<JournalDraft>().AsNoTracking().OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt).ThenByDescending(d => d.Id).Take(ListMax)
            .ToListAsync(ct).ConfigureAwait(false);
        var search = TextNormalizer.CleanText(q.Search);
        var result = new List<JournalEntrySummary>();
        foreach (var d in rows)
        {
            var date = d.EntryDate ?? DateOnly.FromDateTime(d.CreatedAt);
            var month = AccountingCodes.Month(date);
            if (q.PeriodFrom is not null && string.CompareOrdinal(month, q.PeriodFrom) < 0) continue;
            if (q.PeriodTo is not null && string.CompareOrdinal(month, q.PeriodTo) > 0) continue;
            if (search is not null && !(Contains(d.Description, search) || Contains(d.Reference, search))) continue;
            if (accountId is not null || costCentreId is not null || party is not null)
            {
                var lines = Read(d.Lines);
                if (accountId is not null && !lines.Any(l => l.AccountId == accountId)) continue;
                if (costCentreId is not null && !lines.Any(l => l.CostCentreId == costCentreId)) continue;
                if (party is { } pt && !lines.Any(l => l.PartyType == pt.Type && l.PartyId == pt.Id)) continue;
            }
            result.Add(new JournalEntrySummary(tokens.Encode<JournalDraft>(d.PublicId), string.Empty, date, month, d.Description ?? string.Empty,
                AccountingCodes.ModuleAcc, AccountingCodes.SourceManual, true, AccountingCodes.Draft, d.TotalDebit, d.TotalCredit, null, null, d.Reference));
        }
        return result;
    }

    private static bool Contains(string? text, string part) => text is not null && text.Contains(part, StringComparison.OrdinalIgnoreCase);

    private async Task<JournalDraft> FindAsync(string token, CancellationToken ct)
    {
        var publicId = tokens.Decode<JournalDraft>(token) ?? throw Fail.NotFound();
        return await db.Set<JournalDraft>().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }

    /// <summary>Lenient validation (a draft may be incomplete): lengths, line count, amounts not negative, and something to keep.</summary>
    private async Task FillAsync(JournalDraft draft, ManualEntryRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var description = TextNormalizer.CleanText(r.Description);
        var reference = TextNormalizer.CleanText(r.Reference);
        var input = r.Lines ?? [];
        new Validator()
            .MaxLength("description", description, PostingEngine.DescriptionMax)
            .MaxLength("reference", reference, PostingEngine.ReferenceMax)
            .Check(input.Count <= 500, "lines", ErrorCodes.ValidationRange)
            .Check(description is not null || input.Count > 0, "description", ErrorCodes.ValidationRequired)
            .ThrowIfInvalid();
        var lines = new List<DraftLine>(input.Count);
        for (var i = 0; i < input.Count; i++)
        {
            var l = input[i];
            var field = $"lines[{i}]";
            if (l.Debit < 0 || l.Credit < 0) throw Fail.Field(field + ".debit", ErrorCodes.ValidationRange, new FieldRef(field + ".debit"));
            long? accountId = null, centreId = null;
            if (!string.IsNullOrEmpty(l.AccountId))
            {
                var pid = tokens.Decode<Account>(l.AccountId) ?? throw Fail.Field(field + ".accountId", ErrorCodes.ValidationFormat, new FieldRef(field + ".accountId"));
                accountId = await db.Set<Account>().AsNoTracking().Where(a => a.PublicId == pid).Select(a => (long?)a.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? throw Fail.Field(field + ".accountId", ErrorCodes.ValidationFormat, new FieldRef(field + ".accountId"));
            }
            if (!string.IsNullOrEmpty(l.CostCentreId))
            {
                var pid = tokens.Decode<CostCentre>(l.CostCentreId) ?? throw Fail.Field(field + ".costCentreId", ErrorCodes.ValidationFormat, new FieldRef(field + ".costCentreId"));
                centreId = await db.Set<CostCentre>().AsNoTracking().Where(c => c.PublicId == pid).Select(c => (long?)c.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false)
                    ?? throw Fail.Field(field + ".costCentreId", ErrorCodes.ValidationFormat, new FieldRef(field + ".costCentreId"));
            }
            var party = l.Party is { Type: not null } && !string.IsNullOrEmpty(l.Party.Id)
                ? await parties.ResolveAsync(l.Party, field + ".party", ct).ConfigureAwait(false) : null;
            lines.Add(new DraftLine(accountId, l.Debit, l.Credit, centreId, party?.Type, party?.Id, TextNormalizer.CleanText(l.Note)));
        }
        draft.EntryDate = r.EntryDate;
        draft.Description = description;
        draft.Reference = reference;
        draft.Lines = JsonSerializer.Serialize(lines, Json);
        draft.TotalDebit = lines.Sum(l => l.Debit);
        draft.TotalCredit = lines.Sum(l => l.Credit);
    }

    private static List<DraftLine> Read(string json) => JsonSerializer.Deserialize<List<DraftLine>>(json, Json) ?? [];
}
