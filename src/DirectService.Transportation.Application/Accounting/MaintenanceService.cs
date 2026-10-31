using DirectService.Accounting.Contracts.Modules;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Accounting.Domain.Acc;
using DirectService.Transportation.Domain.Common;
using DirectService.Platform.Domain;
using DirectService.Accounting.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Maintenance zone (FR-135, FR-136, acct.maint). Four actions, each previewed first (counts and blockers), confirmed by typing the
/// confirmation word, run in one transaction under the journal-number lock (so no posting runs meanwhile) and logged in
/// core.MaintenanceLogs (user, time, action, counts):
/// <list type="bullet">
/// <item>delete-entries — selected entries without a source document (manual, imported, opening) and their reversal partners;</item>
/// <item>delete-all-entries — every such entry of the company; entries linked to documents are kept and counted;</item>
/// <item>replace-chart — only while the company has no entries, pending postings, AP/AR documents or accounting parties: the chart,
/// the account mapping, cash boxes and bank accounts are deleted and the approved template is applied (or the chart is left empty for
/// an import);</item>
/// <item>reset — only while no entry is linked to a document, no period is closed and nothing waits to be posted: entries, opening
/// balances and periods are cleared and the company returns to initial setup.</item>
/// </list>
/// Every deletion is a soft deletion (records flagged, excluded everywhere, kept in the database); refused in closed periods and for
/// entries linked to documents. A JSON backup of the accounting data is offered first (also logged).
/// </summary>
public sealed class MaintenanceService(IAppDbContext db, IRequestContext ctx, INumberingService numbering, IChartOfAccountsSetup chart, AuditTrail audit,
    IUrlTokenService tokens, TimeProvider clock)
{
    public const string WordAr = "حذف";
    public const string WordEn = "DELETE";

    private bool En => ctx.Language == "en";

    private string Word => En ? WordEn : WordAr;

    private long UserId => ctx.UserId ?? throw new InvalidOperationException("Maintenance needs a user.");

    private static string Action(string action) => action switch
    {
        MaintenanceActions.DeleteEntries => "DeleteSelectedEntries",
        MaintenanceActions.DeleteAllEntries => "DeleteAllEntries",
        MaintenanceActions.ReplaceChart => "ReplaceChart",
        MaintenanceActions.Reset => "FullReset",
        _ => throw Fail.NotFound(),
    };

    // ============================================================ preview

    private sealed record Plan(Dictionary<string, int> Counts, int LinkedKept, List<string> Blockers, List<long> EntryIds);

    public async Task<MaintenancePreview> PreviewAsync(string action, MaintenanceRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var plan = await PlanAsync(action, r, ct).ConfigureAwait(false);
        return new MaintenancePreview(action, plan.Counts, plan.LinkedKept, plan.Blockers, Word);
    }

    private async Task<List<string>> ClosedMonthsAsync(CancellationToken ct) =>
        await db.Set<Period>().AsNoTracking().Where(p => p.Status == "Closed").Select(p => p.PeriodMonth).ToListAsync(ct).ConfigureAwait(false);

    private async Task<Plan> PlanAsync(string action, MaintenanceRequest r, CancellationToken ct)
    {
        Action(action);
        var blockers = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var closed = await ClosedMonthsAsync(ct).ConfigureAwait(false);
        var entries = db.Set<JournalEntry>().AsNoTracking();
        var linkedCount = await entries.CountAsync(e => e.SourceId != null, ct).ConfigureAwait(false);
        List<long> ids = [];
        var linkedKept = 0;

        switch (action)
        {
            case MaintenanceActions.DeleteEntries:
            {
                var requested = r.EntryIds ?? [];
                if (requested.Count == 0) throw Fail.Field("entryIds", ErrorCodes.ValidationRequired, new FieldRef("entryIds"));
                if (requested.Count > 5000) throw Fail.Field("entryIds", ErrorCodes.ValidationRange, new FieldRef("entryIds"));
                var pids = requested.Select(t => tokens.Decode<JournalEntry>(t) ?? throw Fail.Field("entryIds", ErrorCodes.ValidationFormat, new FieldRef("entryIds"))).Distinct().ToList();
                var chosen = await entries.Where(e => pids.Contains(e.PublicId)).Select(e => new { e.Id, e.SourceId, e.SourceType, e.ReversesEntryId, e.ReversedByEntryId })
                    .ToListAsync(ct).ConfigureAwait(false);
                if (chosen.Count != pids.Count) throw Fail.Field("entryIds", ErrorCodes.ValidationFormat, new FieldRef("entryIds"));
                // The opening entry belongs to the opening balances (cleared only by a full reset, review L6).
                var linked = chosen.Count(e => e.SourceId != null || e.SourceType == AccountingCodes.SourceOpening);
                if (linked > 0) blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceLinked, ctx.Language, linked));
                // A reversal and the entry it reverses go together, so the remaining ledger still nets.
                var set = chosen.Select(e => e.Id).ToHashSet();
                foreach (var e in chosen)
                {
                    if (e.ReversesEntryId is long a) set.Add(a);
                    if (e.ReversedByEntryId is long b) set.Add(b);
                }
                ids = [.. set];
                break;
            }
            case MaintenanceActions.DeleteAllEntries:
                ids = await entries.Where(e => e.SourceId == null && e.SourceType != AccountingCodes.SourceOpening).Select(e => e.Id).ToListAsync(ct).ConfigureAwait(false);
                linkedKept = linkedCount + await entries.CountAsync(e => e.SourceId == null && e.SourceType == AccountingCodes.SourceOpening, ct).ConfigureAwait(false);
                if (ids.Count == 0) blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceNothing, ctx.Language));
                break;
            case MaintenanceActions.ReplaceChart:
            {
                var entryCount = await entries.CountAsync(ct).ConfigureAwait(false);
                var pending = await db.Set<PendingPosting>().CountAsync(p => p.ResolvedAt == null, ct).ConfigureAwait(false);
                var documents = await db.Set<ApArDocument>().CountAsync(ct).ConfigureAwait(false);
                var partiesCount = await db.Set<ApArParty>().CountAsync(ct).ConfigureAwait(false);
                if (entryCount > 0) blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceChartInUse, ctx.Language, Count(entryCount, "قيود", "journal entries")));
                if (pending > 0) blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceChartInUse, ctx.Language, Count(pending, "ترحيلات معلّقة", "pending postings")));
                if (documents > 0) blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceChartInUse, ctx.Language, Count(documents, "مستندات ذمم", "payable/receivable documents")));
                if (partiesCount > 0) blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceChartInUse, ctx.Language, Count(partiesCount, "أطراف محاسبية", "accounting parties")));
                counts["Accounts"] = await db.Set<Account>().CountAsync(ct).ConfigureAwait(false);
                counts["PostingKeyMappings"] = await db.Set<PostingKeyMapping>().CountAsync(ct).ConfigureAwait(false);
                counts["CashBoxes"] = await db.Set<CashBox>().CountAsync(ct).ConfigureAwait(false);
                counts["BankAccounts"] = await db.Set<BankAccount>().CountAsync(ct).ConfigureAwait(false);
                if (r.ChartSource is not null && r.ChartSource.Trim().ToLowerInvariant() is not ("template" or "empty"))
                    throw Fail.Field("chartSource", ErrorCodes.ValidationFormat, new FieldRef("chartSource"));
                return new Plan(counts, 0, blockers, []);
            }
            case MaintenanceActions.Reset:
            {
                if (linkedCount > 0) blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceResetBlocked, ctx.Language, Count(linkedCount, "قيود مرتبطة بمستندات", "entries linked to documents")));
                if (closed.Count > 0) blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceResetBlocked, ctx.Language, (En ? "closed periods " : "فترات مغلقة ") + string.Join(", ", closed.Order(StringComparer.Ordinal))));
                var pending = await db.Set<PendingPosting>().CountAsync(p => p.ResolvedAt == null, ct).ConfigureAwait(false);
                if (pending > 0) blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceResetBlocked, ctx.Language, Count(pending, "ترحيلات معلّقة", "pending postings")));
                ids = await entries.Select(e => e.Id).ToListAsync(ct).ConfigureAwait(false);
                counts["Periods"] = await db.Set<Period>().CountAsync(ct).ConfigureAwait(false);
                counts["OpeningBalances"] = await db.Set<Account>().CountAsync(a => a.OpeningDebit != 0 || a.OpeningCredit != 0, ct).ConfigureAwait(false);
                break;
            }
        }

        // Closed periods block every deletion dated in them (FR-133, FR-136).
        if (ids.Count > 0 && action != MaintenanceActions.Reset)
        {
            var inClosed = await entries.Where(e => ids.Contains(e.Id) && closed.Contains(e.PeriodMonth)).Select(e => e.PeriodMonth).ToListAsync(ct).ConfigureAwait(false);
            if (inClosed.Count > 0)
                blockers.Add(ErrorCatalog.Format(ErrorCodes.MaintenanceClosedPeriod, ctx.Language, inClosed.Count, string.Join(", ", inClosed.Distinct().Order(StringComparer.Ordinal))));
        }
        counts["JournalEntries"] = ids.Count;
        counts["EntryLines"] = ids.Count == 0 ? 0 : await db.Set<EntryLine>().CountAsync(l => ids.Contains(l.JournalEntryId), ct).ConfigureAwait(false);
        return new Plan(counts, linkedKept, blockers, ids);
    }

    private string Count(int n, string ar, string en) => n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (En ? en : ar);

    // ============================================================ run

    public async Task<MaintenanceResult> RunAsync(string action, MaintenanceRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = Action(action);
        var typed = r.Confirmation?.Trim();
        if (!string.Equals(typed, WordAr, StringComparison.Ordinal) && !string.Equals(typed, WordEn, StringComparison.OrdinalIgnoreCase))
            throw Fail.RuleOn("confirmation", ErrorCodes.MaintenanceConfirmation, Word);
        var reason = TextNormalizer.CleanText(r.Reason) ?? (En ? "Maintenance zone: " : "منطقة الصيانة: ") + name;
        if (reason.Length > 450) throw Fail.Field("reason", ErrorCodes.ValidationRange, new FieldRef("reason"));
        var now = clock.GetUtcNow().UtcDateTime;
        var user = UserId;
        Dictionary<string, int> counts = [];
        await db.InTransactionAsync(async c =>
        {
            // Postings take this lock for their number: none runs while the action is checked and applied (as closing a period, BR-036).
            await numbering.LockAsync(DocumentTypes.JournalEntry, c).ConfigureAwait(false);
            var plan = await PlanAsync(action, r, c).ConfigureAwait(false);
            if (plan.Blockers.Count > 0) throw Fail.Rule(ErrorCodes.MaintenanceRefused, string.Join(" · ", plan.Blockers));
            counts = plan.Counts;
            var deleteReason = "Maintenance: " + reason;
            if (plan.EntryIds.Count > 0)
            {
                // Lines first: once their entries are flagged, the entries no longer join.
                foreach (var chunk in plan.EntryIds.Chunk(2000))
                {
                    await SoftDelete(db.Set<EntryLine>().Where(l => chunk.Contains(l.JournalEntryId)), deleteReason, user, now, c).ConfigureAwait(false);
                    await SoftDelete(db.Set<JournalEntry>().Where(e => chunk.Contains(e.Id)), deleteReason, user, now, c).ConfigureAwait(false);
                }
            }
            switch (action)
            {
                case MaintenanceActions.ReplaceChart:
                    await SoftDelete(db.Set<PostingKeyMapping>(), deleteReason, user, now, c).ConfigureAwait(false);
                    await SoftDelete(db.Set<CashBox>(), deleteReason, user, now, c).ConfigureAwait(false);
                    await SoftDelete(db.Set<BankAccount>(), deleteReason, user, now, c).ConfigureAwait(false);
                    await SoftDelete(db.Set<Account>(), deleteReason, user, now, c).ConfigureAwait(false);
                    db.ChangeTracker.Clear();
                    if (r.ChartSource?.Trim().ToLowerInvariant() != "empty")
                    {
                        var applied = await chart.ApplyTemplateAsync(c).ConfigureAwait(false);
                        counts["TemplateAccounts"] = applied.Accounts;
                    }
                    break;
                case MaintenanceActions.Reset:
                    await db.Set<Account>().Where(a => a.OpeningDebit != 0 || a.OpeningCredit != 0)
                        .ExecuteUpdateAsync(u => u.SetProperty(a => a.OpeningDebit, 0m).SetProperty(a => a.OpeningCredit, 0m).SetProperty(a => a.UpdatedAt, now)
                            .SetProperty(a => a.UpdatedBy, user), c).ConfigureAwait(false);
                    await SoftDelete(db.Set<Period>(), deleteReason, user, now, c).ConfigureAwait(false);
                    await db.Set<Company>().Where(x => x.Id == ctx.CompanyId).ExecuteUpdateAsync(u => u.SetProperty(x => x.MigrationCompletedAt, (DateTime?)null)
                        .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, user), c).ConfigureAwait(false);
                    break;
            }
            await LogAsync(name, counts, reason, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return new MaintenanceResult(action, counts, DateTime.SpecifyKind(now, DateTimeKind.Utc));
    }

    private static Task<int> SoftDelete<T>(IQueryable<T> q, string reason, long user, DateTime now, CancellationToken ct) where T : AuditableEntity =>
        q.ExecuteUpdateAsync(u => u.SetProperty(x => x.IsDeleted, true).SetProperty(x => x.DeletedAt, now).SetProperty(x => x.DeletedBy, user)
            .SetProperty(x => x.DeleteReason, reason).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, user), ct);

    /// <summary>Append-only maintenance log row plus one audit-log row pointing at it (the bulk updates themselves are not audited row by row, DT-105).</summary>
    private async Task LogAsync(string action, IReadOnlyDictionary<string, int> counts, string? reason, CancellationToken ct)
    {
        var log = new MaintenanceLog
        {
            TenantId = ctx.TenantId ?? 0, CompanyId = ctx.CompanyId ?? 0, UserId = UserId, Action = action, RecordCounts = JsonSerializer.Serialize(counts),
            OccurredAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Set<MaintenanceLog>().Add(log);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        audit.Record("CORE.MaintenanceLog", log.Id, action, reason, counts.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ============================================================ backup and log

    /// <summary>
    /// JSON backup of the company's accounting data (FR-136 "backup offered first"): chart with openings, mapping, cost centres, cash boxes,
    /// bank accounts, periods and every entry with its lines. Logged as a maintenance action.
    /// </summary>
    public async Task<GeneratedFile> BackupAsync(CancellationToken ct)
    {
        var accounts = await db.Set<Account>().AsNoTracking().OrderBy(a => a.Code)
            .Select(a => new { a.Code, a.NameAr, a.NameEn, a.AccountType, Parent = a.Parent != null ? a.Parent.Code : null, a.IsPostable, a.IsControlAccount, a.ControlPartyType,
                a.OpeningDebit, a.OpeningCredit, a.Status }).ToListAsync(ct).ConfigureAwait(false);
        var mapping = await db.Set<PostingKeyMapping>().AsNoTracking().Select(m => new { m.PostingKey, Account = m.Account.Code }).ToListAsync(ct).ConfigureAwait(false);
        var centres = await db.Set<CostCentre>().AsNoTracking().Select(c => new { c.Code, c.Name, c.Status }).ToListAsync(ct).ConfigureAwait(false);
        var boxes = await db.Set<CashBox>().AsNoTracking().Select(b => new { b.Name, Account = b.Account.Code, b.Status }).ToListAsync(ct).ConfigureAwait(false);
        var banks = await db.Set<BankAccount>().AsNoTracking().Select(b => new { b.Name, b.BankName, b.AccountNumber, b.Iban, b.CurrencyCode, Account = b.Account.Code, b.Status })
            .ToListAsync(ct).ConfigureAwait(false);
        var periods = await db.Set<Period>().AsNoTracking().Select(p => new { p.PeriodMonth, p.Status, p.ClosedAt, p.Reason }).ToListAsync(ct).ConfigureAwait(false);
        var entries = await db.Set<JournalEntry>().AsNoTracking().OrderBy(e => e.Id)
            .Select(e => new
            {
                e.Number, e.EntryDate, e.Description, e.SourceModule, e.SourceType, e.SourceId, e.IsManual, e.Status, e.TotalDebit, e.TotalCredit,
                Reverses = e.ReversesEntry != null ? e.ReversesEntry.Number : null,
                Lines = e.Lines.OrderBy(l => l.LineNo).Select(l => new { l.LineNo, Account = l.Account.Code, l.Debit, l.Credit, CostCentre = l.CostCentre != null ? l.CostCentre.Code : null,
                    l.PartyModule, l.PartyType, l.PartyId, l.Note }).ToList(),
            }).ToListAsync(ct).ConfigureAwait(false);
        var company = await db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => c.Name).SingleAsync(ct).ConfigureAwait(false);
        var payload = new { company, generatedAt = clock.GetUtcNow().UtcDateTime, accounts, mapping, costCentres = centres, cashBoxes = boxes, bankAccounts = banks, periods, entries };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await LogAsync("Backup", new Dictionary<string, int> { ["Accounts"] = accounts.Count, ["JournalEntries"] = entries.Count }, null, ct).ConfigureAwait(false);
        return new GeneratedFile("accounting-backup-" + clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + ".json",
            "application/json", bytes);
    }

    public async Task<IReadOnlyList<MaintenanceLogEntry>> LogAsync(CancellationToken ct)
    {
        var rows = await db.Set<MaintenanceLog>().AsNoTracking().Where(l => l.CompanyId == ctx.CompanyId).OrderByDescending(l => l.Id).Take(200)
            .Select(l => new { l.OccurredAt, User = l.User.FullName, l.Action, l.RecordCounts }).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(l => new MaintenanceLogEntry(DateTime.SpecifyKind(l.OccurredAt, DateTimeKind.Utc), l.User, l.Action,
            JsonSerializer.Deserialize<Dictionary<string, int>>(l.RecordCounts) ?? [])).ToList();
    }
}
