using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Accounting.Domain.Acc;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Journal entry templates (نماذج القيود; revision 6 part B, owner change request 15 Oct 2026): "Save as template" on the new journal entry
/// screen keeps a name, the description, Reference and the lines (account, cost centre, line description, optional amounts). "Use template"
/// fills a new entry, which the user edits and saves as a draft or posts as usual — every posting rule applies then. Templates are per
/// company; renamed, edited and deleted from the templates list. No automatic or recurring posting.
/// </summary>
public sealed class JournalTemplateService(IAppDbContext db, MasterDataSupport support, IUrlTokenService tokens, PostingEngine engine)
{
    public const string EntityType = "ACC.JournalTemplate";
    public const int NameMax = 100;
    public const int MaxLines = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A stored line: internal ids, so a template survives a change of the URL-token key; line reference and tax since DT-254 / DT-255.</summary>
    private sealed record TemplateLine(long AccountId, long? CostCentreId, string? Note, decimal Debit, decimal Credit,
        string? Reference = null, long? TaxCodeId = null, long? TaxAccountId = null);

    public async Task<IReadOnlyList<JournalTemplateSummary>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Set<JournalTemplate>().AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct).ConfigureAwait(false);
        var userIds = rows.Select(r => r.UpdatedBy ?? r.CreatedBy).Where(u => u is not null).Select(u => u!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct).ConfigureAwait(false);
        return rows.Select(t =>
        {
            var lines = Read(t.Lines);
            return new JournalTemplateSummary(tokens.Encode<JournalTemplate>(t.PublicId), t.Name, t.Description, t.Reference, lines.Count,
                lines.Any(l => l.Debit != 0 || l.Credit != 0), DateTime.SpecifyKind(t.UpdatedAt ?? t.CreatedAt, DateTimeKind.Utc),
                (t.UpdatedBy ?? t.CreatedBy) is long u ? names.GetValueOrDefault(u) : null, Convert.ToBase64String(t.RowVersion));
        }).ToList();
    }

    public async Task<JournalTemplateResponse> GetAsync(string token, CancellationToken ct)
    {
        var t = await support.FindAsync<JournalTemplate>(token, ct).ConfigureAwait(false);
        var lines = Read(t.Lines);
        var accountIds = lines.Select(l => l.AccountId).Concat(lines.Where(l => l.TaxAccountId is not null).Select(l => l.TaxAccountId!.Value)).Distinct().ToList();
        var accounts = await db.Set<Account>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new AccountRef(tokens.Encode<Account>(a.PublicId), a.Code, a.NameAr, a.NameEn), ct).ConfigureAwait(false);
        var centreIds = lines.Where(l => l.CostCentreId is not null).Select(l => l.CostCentreId!.Value).Distinct().ToList();
        var centres = await db.Set<CostCentre>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(c => centreIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new Ref(tokens.Encode<CostCentre>(c.PublicId), $"{c.Code} – {c.Name}"), ct).ConfigureAwait(false);
        var codeIds = lines.Where(l => l.TaxCodeId is not null).Select(l => l.TaxCodeId!.Value).Distinct().ToList();
        var codes = await db.Set<TaxCode>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(x => codeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new TaxCodeRef(tokens.Encode<TaxCode>(x.PublicId), x.Code, x.Rate, x.NameAr, x.NameEn), ct).ConfigureAwait(false);
        return new JournalTemplateResponse(token, t.Name, t.Description, t.Reference,
            lines.Select(l => new JournalTemplateLineResponse(accounts.GetValueOrDefault(l.AccountId), l.Debit, l.Credit,
                l.CostCentreId is long c ? centres.GetValueOrDefault(c) : null, l.Note, l.Reference,
                l.TaxCodeId is long tc ? codes.GetValueOrDefault(tc) : null, l.TaxAccountId is long ta ? accounts.GetValueOrDefault(ta) : null)).ToList(),
            DateTime.SpecifyKind(t.UpdatedAt ?? t.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(t.RowVersion));
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var t = await support.FindAsync<JournalTemplate>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, t.Id, ct).ConfigureAwait(false);
    }

    public async Task<JournalTemplateResponse> CreateAsync(JournalTemplateRequest r, CancellationToken ct)
    {
        var t = new JournalTemplate();
        await FillAsync(t, r, null, ct).ConfigureAwait(false);
        db.Set<JournalTemplate>().Add(t);
        await SaveAsync(t, ct).ConfigureAwait(false);
        return await GetAsync(tokens.Encode<JournalTemplate>(t.PublicId), ct).ConfigureAwait(false);
    }

    /// <summary>Saves a template: name (rename), description, Reference and every line.</summary>
    public async Task<JournalTemplateResponse> UpdateAsync(string token, JournalTemplateRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var t = await support.FindAsync<JournalTemplate>(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(t, r.RowVersion);
        await FillAsync(t, r, t.Id, ct).ConfigureAwait(false);
        await SaveAsync(t, ct).ConfigureAwait(false);
        return await GetAsync(token, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var t = await support.FindAsync<JournalTemplate>(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(t, rowVersion);
        t.DeleteReason = "Template deleted";
        db.Set<JournalTemplate>().Remove(t);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task SaveAsync(JournalTemplate t, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another user saved a template with this name at the same moment (UX_JournalTemplates_CompanyId_Name).
            if (await db.Set<JournalTemplate>().AsNoTracking().AnyAsync(x => x.Name == t.Name && x.Id != t.Id, ct).ConfigureAwait(false))
                throw Fail.Field("name", ErrorCodes.TemplateNameTaken, t.Name);
            throw;
        }
    }

    /// <summary>
    /// Checks and stores the template: a name (unique in the company), description and Reference within the entry's limits, 1–500 lines, each
    /// with an account of the company (posting rules are checked when an entry made from it is posted), amounts not negative and not both a
    /// debit and a credit, an existing cost centre.
    /// </summary>
    private async Task FillAsync(JournalTemplate t, JournalTemplateRequest r, long? self, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = TextNormalizer.CleanText(r.Name);
        var description = TextNormalizer.CleanText(r.Description);
        var reference = TextNormalizer.CleanText(r.Reference);
        var input = r.Lines ?? [];
        new Validator()
            .Require("name", name)
            .MaxLength("name", name, NameMax)
            .MaxLength("description", description, DirectService.Accounting.Contracts.Posting.PostingRules.DescriptionMax)
            .MaxLength("reference", reference, DirectService.Accounting.Contracts.Posting.PostingRules.ReferenceMax)
            .Check(input.Count is > 0 and <= MaxLines, "lines", input.Count == 0 ? ErrorCodes.ValidationRequired : ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        if (await db.Set<JournalTemplate>().AnyAsync(x => x.Name == name && x.Id != self, ct).ConfigureAwait(false))
            throw Fail.Field("name", ErrorCodes.TemplateNameTaken, name!);
        var decimals = await engine.CurrencyDecimalsAsync(ct).ConfigureAwait(false);
        var accounts = await db.Set<Account>().AsNoTracking().Select(a => new { a.Id, a.PublicId }).ToDictionaryAsync(a => a.PublicId, a => a.Id, ct).ConfigureAwait(false);
        var centres = await db.Set<CostCentre>().AsNoTracking().Select(c => new { c.Id, c.PublicId }).ToDictionaryAsync(c => c.PublicId, c => c.Id, ct).ConfigureAwait(false);
        var taxCodes = await db.Set<TaxCode>().AsNoTracking().Select(c => new { c.Id, c.PublicId }).ToDictionaryAsync(c => c.PublicId, c => c.Id, ct).ConfigureAwait(false);
        var lines = new List<TemplateLine>(input.Count);
        for (var i = 0; i < input.Count; i++)
        {
            var l = input[i];
            var field = $"lines[{i}]";
            var accountId = tokens.Decode<Account>(l.AccountId) is Guid g && accounts.TryGetValue(g, out var a) ? a
                : throw Fail.Field(field + ".accountId", string.IsNullOrEmpty(l.AccountId) ? ErrorCodes.ValidationRequired : ErrorCodes.ValidationFormat, new FieldRef(field + ".accountId"));
            if (!Money.IsValidAmount(l.Debit, decimals)) throw Fail.Field(field + ".debit", ErrorCodes.ValidationRange, new FieldRef(field + ".debit"));
            if (!Money.IsValidAmount(l.Credit, decimals) || (l.Debit > 0 && l.Credit > 0)) throw Fail.Field(field + ".credit", ErrorCodes.ValidationRange, new FieldRef(field + ".credit"));
            long? centreId = null;
            if (!string.IsNullOrEmpty(l.CostCentreId))
                centreId = tokens.Decode<CostCentre>(l.CostCentreId) is Guid cg && centres.TryGetValue(cg, out var c) ? c
                    : throw Fail.Field(field + ".costCentreId", ErrorCodes.ValidationFormat, new FieldRef(field + ".costCentreId"));
            var note = TextNormalizer.CleanText(l.Note);
            if (note is { Length: > 500 }) throw Fail.Field(field + ".note", ErrorCodes.ValidationRange, new FieldRef(field + ".note"));
            var lineReference = TextNormalizer.CleanText(l.Reference);
            if (lineReference is { Length: > DirectService.Accounting.Contracts.Posting.PostingRules.ReferenceMax }) throw Fail.Field(field + ".reference", ErrorCodes.ValidationRange, new FieldRef(field + ".reference"));
            long? taxCodeId = null, taxAccountId = null;
            if (!string.IsNullOrEmpty(l.TaxCodeId))
                taxCodeId = tokens.Decode<TaxCode>(l.TaxCodeId) is Guid tg && taxCodes.TryGetValue(tg, out var tc) ? tc
                    : throw Fail.Field(field + ".taxCodeId", ErrorCodes.ValidationFormat, new FieldRef(field + ".taxCodeId"));
            if (taxCodeId is not null && !string.IsNullOrEmpty(l.TaxAccountId))
                taxAccountId = tokens.Decode<Account>(l.TaxAccountId) is Guid tag && accounts.TryGetValue(tag, out var ta) ? ta
                    : throw Fail.Field(field + ".taxAccountId", ErrorCodes.ValidationFormat, new FieldRef(field + ".taxAccountId"));
            lines.Add(new TemplateLine(accountId, centreId, note, l.Debit, l.Credit, lineReference, taxCodeId, taxAccountId));
        }
        t.Name = name!;
        t.Description = description;
        t.Reference = reference;
        var json = JsonSerializer.Serialize(lines, Json);
        if (json != t.Lines) t.Lines = json;
    }

    private static List<TemplateLine> Read(string json) => JsonSerializer.Deserialize<List<TemplateLine>>(json, Json) ?? [];
}
