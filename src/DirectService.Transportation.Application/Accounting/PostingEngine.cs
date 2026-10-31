using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Accounting.Domain.Acc;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>A party on a posting line, by internal id (validated by the caller): module TRN/ACC + type + id.</summary>
public sealed record PostingParty(string Module, string Type, long Id);

/// <summary>
/// One line to post: either a posting key (resolved through acc.PostingKeyMappings, 7.5.1) or an explicit account
/// (cash box / bank account / manual entries). Exactly one of <see cref="Debit"/> and <see cref="Credit"/> is &gt; 0.
/// Manual lines (owner request 25 Oct 2026) may carry a line <see cref="Reference"/> (DT-254; none = the entry's Reference) and a tax code with
/// its tax account (DT-255): the amount is before tax and the engine adds the tax as an automatic line on the same side.
/// </summary>
public sealed record PostingLine(string? PostingKey, long? AccountId, decimal Debit, decimal Credit, long? CostCentreId = null,
    PostingParty? Party = null, string? Note = null, string? Reference = null, long? TaxCodeId = null, long? TaxAccountId = null)
{
    public static PostingLine DebitKey(string key, decimal amount, PostingParty? party = null) => new(key, null, amount, 0, null, party);

    public static PostingLine CreditKey(string key, decimal amount, PostingParty? party = null) => new(key, null, 0, amount, null, party);
}

/// <summary>An entry to post (FR-097): date = source document date (BR-034), description, source link, lines.</summary>
public sealed record PostingRequest(DateOnly EntryDate, string Description, string SourceModule, string? SourceType, long? SourceId,
    bool IsManual, IReadOnlyList<PostingLine> Lines, string? Reference = null);

/// <summary>
/// The posting engine (PRD 8.6, ADR-09). Every method runs inside the caller's transaction so the document, its entry,
/// the number and the audit rows commit together (FR-096, NFR-04).
/// <list type="bullet">
/// <item>Business failures — unbalanced (BR-033), closed period (BR-034/036), unmapped key (FR-094), inactive or
/// non-postable account (FR-093), missing control party (FR-106) — refuse the whole operation (FR-098).</item>
/// <item><see cref="PostOrPendAsync"/>: a technical failure rolls back to a savepoint, the document is kept and the
/// entry waits in acc.PendingPostings with the reason (FR-101).</item>
/// <item>Reversal: a mirror entry linked both ways; the original is never changed apart from its status (FR-099, BR-035).</item>
/// <item>Tax (DT-255): a line with a tax code of rate &gt; 0 gets an automatic tax line to its tax account, on the same side, with its cost
/// centre and reference, linked to it (TaxForLineId); the entry must balance with the tax lines.</item>
/// </list>
/// </summary>
public sealed partial class PostingEngine(IAppDbContext db, INumberingService numbering, IPeriodGuard periods, IRequestContext ctx,
    IChangeContext changes, TimeProvider clock, DirectService.Platform.Contracts.Collaboration.IUserNotifier? notifications = null, ILogger<PostingEngine>? log = null)
{
    public const int DescriptionMax = 500;

    /// <summary>Reference / المرجع of an entry (revision 6).</summary>
    public const int ReferenceMax = 100;
    public const int NoteMax = 300;
    private const string Savepoint = "before_posting";

    // ------------------------------------------------------------ readiness (FR-094)

    /// <summary>True while the company is in initial setup / data migration (FR-110): migration not marked complete and no period ever closed.</summary>
    public async Task<bool> IsSetupOpenAsync(CancellationToken ct)
    {
        var completed = await db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId)
            .Select(c => c.MigrationCompletedAt).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (completed is not null) return false;
        // A period that was closed at least once keeps ClosedAt (reopen stamps it again), DT-23.
        return !await db.Set<Period>().AsNoTracking().AnyAsync(p => p.ClosedAt != null, ct).ConfigureAwait(false);
    }

    /// <summary>Required posting keys that are not mapped (FR-094).</summary>
    public async Task<IReadOnlyList<string>> MissingKeysAsync(CancellationToken ct)
    {
        var setupOpen = await IsSetupOpenAsync(ct).ConfigureAwait(false);
        var mapped = await db.Set<PostingKeyMapping>().AsNoTracking().Select(m => m.PostingKey).ToListAsync(ct).ConfigureAwait(false);
        return PostingKeys.Required(setupOpen).Where(k => !mapped.Contains(k)).ToList();
    }

    /// <summary>Refuses automatic posting and invoicing while a required key is unmapped (FR-094).</summary>
    public async Task EnsurePostingReadyAsync(CancellationToken ct)
    {
        var missing = await MissingKeysAsync(ct).ConfigureAwait(false);
        if (missing.Count > 0) throw Fail.Rule(ErrorCodes.PostingKeysMissing, PostingKeys.NameList(missing, ctx.Language));   // names, not codes (DT-224)
    }

    public async Task<int> CurrencyDecimalsAsync(CancellationToken ct) =>
        await db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => (int)c.CurrencyDecimals)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);

    // ------------------------------------------------------------ posting

    /// <summary>Validates and posts an entry; business failures throw (FR-098). Must run inside a transaction.</summary>
    public Task<JournalEntry> PostAsync(PostingRequest request, CancellationToken ct) => PostAsync(request, "Post", null, ct);

    /// <summary>As <see cref="PostAsync(PostingRequest, CancellationToken)"/>, recording a specific audit action and note (e.g. an edit, D-05).</summary>
    public async Task<JournalEntry> PostAsync(PostingRequest request, string auditAction, string? auditReason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTransaction();
        var decimals = await CurrencyDecimalsAsync(ct).ConfigureAwait(false);
        var description = TextNormalizer.CleanText(request.Description);
        var reference = TextNormalizer.CleanText(request.Reference);
        new Validator().Require("description", description).MaxLength("description", description, DescriptionMax)
            .MaxLength("reference", reference, ReferenceMax).ThrowIfInvalid();
        if (request.Lines.Count < 2) throw Fail.Rule(ErrorCodes.JournalMinLines);

        var automatic = !request.IsManual && request.SourceType != AccountingCodes.SourceOpening;
        if (automatic) await EnsurePostingReadyAsync(ct).ConfigureAwait(false);

        // Posting keys → accounts (mapping changes apply to new entries only, FR-095).
        var keys = request.Lines.Where(l => l.PostingKey is not null).Select(l => l.PostingKey!).Distinct().ToList();
        var mapping = await db.Set<PostingKeyMapping>().AsNoTracking().Where(m => keys.Contains(m.PostingKey))
            .ToDictionaryAsync(m => m.PostingKey, m => m.AccountId, ct).ConfigureAwait(false);
        var unmapped = keys.Where(k => !mapping.ContainsKey(k)).ToList();
        if (unmapped.Count > 0) throw Fail.Rule(ErrorCodes.PostingKeysMissing, PostingKeys.NameList(unmapped, ctx.Language));

        var resolved = request.Lines.Select(l => l with { AccountId = l.PostingKey is null ? l.AccountId : mapping[l.PostingKey] }).ToList();
        var accountIds = resolved.Select(l => l.AccountId ?? 0).Distinct().ToList();
        var accounts = await db.Set<Account>().AsNoTracking().Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.Status, a.IsPostable, a.IsControlAccount, a.ControlPartyType, HasChildren = a.Children.Any() })
            .ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);

        var costCentreIds = resolved.Where(l => l.CostCentreId is not null).Select(l => l.CostCentreId!.Value).Distinct().ToList();
        var costCentres = await db.Set<CostCentre>().AsNoTracking().Where(c => costCentreIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Status), ct).ConfigureAwait(false);

        var taxCodeIds = resolved.Where(l => l.TaxCodeId is not null).Select(l => l.TaxCodeId!.Value).Distinct().ToList();
        var taxCodes = taxCodeIds.Count == 0 ? [] : await db.Set<TaxCode>().AsNoTracking().Where(t => taxCodeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct).ConfigureAwait(false);
        var taxAccountIds = resolved.Where(l => l.TaxAccountId is not null).Select(l => l.TaxAccountId!.Value).Distinct().ToList();
        var taxAccounts = await db.Set<Account>().AsNoTracking().Where(a => taxAccountIds.Contains(a.Id))
            .Select(a => new TaxAccountRow(a.Id, a.Code, a.Status, a.IsPostable, a.IsTaxAccount, a.IsControlAccount, a.Children.Any()))
            .ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);

        var apArIds = resolved.Where(l => l.Party is { Type: AccountingCodes.PartyApAr }).Select(l => l.Party!.Id).Distinct().ToList();
        var apArRoles = await db.Set<ApArParty>().AsNoTracking().Where(p => apArIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.PartyRole, ct).ConfigureAwait(false);

        decimal totalDebit = 0, totalCredit = 0, taxDebit = 0, taxCredit = 0;
        var taxes = new (TaxCode Code, decimal Amount, long? AccountId)?[resolved.Count];
        for (var i = 0; i < resolved.Count; i++)
        {
            var l = resolved[i];
            var lineNo = i + 1;
            var amountsOk = (l.Debit > 0) != (l.Credit > 0) && Money.IsValidAmount(l.Debit, decimals) && Money.IsValidAmount(l.Credit, decimals);
            if (!amountsOk) throw Fail.Rule(ErrorCodes.JournalLineAmount, lineNo);
            if (l.AccountId is not long accountId || !accounts.TryGetValue(accountId, out var account))
                throw Fail.Rule(ErrorCodes.AccountNotPostable, "#" + lineNo);
            if (account.Status != AccountingCodes.Active || !account.IsPostable || account.HasChildren)
                throw Fail.Rule(ErrorCodes.AccountNotPostable, account.Code);
            if (l.CostCentreId is long cc && (!costCentres.TryGetValue(cc, out var centre) || centre.Status != AccountingCodes.Active))
                throw Fail.Rule(ErrorCodes.RecordArchived, costCentres.TryGetValue(cc, out var c2) ? c2.Code : "#" + lineNo);
            if (account.IsControlAccount)
            {
                // FR-106 / G-17: control-account lines name a party matching the account's party type. The opening entry
                // carries account-level openings, which have no party (DT-24).
                if (l.Party is null && request.SourceType != AccountingCodes.SourceOpening)
                    throw Fail.Rule(ErrorCodes.ControlPartyRequired, lineNo, account.Code, account.ControlPartyType);
                if (l.Party is not null && !PartyMatches(account.ControlPartyType, l.Party, apArRoles))
                    throw Fail.Rule(ErrorCodes.ControlPartyMismatch, lineNo, account.Code, account.ControlPartyType);
            }
            if (l.Note is { Length: > NoteMax }) throw Fail.Field("note", ErrorCodes.ValidationRange, new FieldRef("note"));
            if (TextNormalizer.CleanText(l.Reference) is { Length: > ReferenceMax })
                throw Fail.Field($"lines[{i}].reference", ErrorCodes.ValidationRange, new FieldRef($"lines[{i}].reference"));
            totalDebit += l.Debit;
            totalCredit += l.Credit;
            if (l.TaxCodeId is long taxCodeId)
            {
                var tax = TaxOf(l, i, taxCodeId, taxCodes, taxAccounts, decimals);
                taxes[i] = tax;
                if (l.Debit > 0) { totalDebit += tax.Amount; taxDebit += tax.Amount; }
                else { totalCredit += tax.Amount; taxCredit += tax.Amount; }
            }
        }
        if (totalDebit != totalCredit)
        {
            // With tax the message names the tax on each side: it is added on the same side as its line (DT-255).
            if (taxDebit + taxCredit > 0)
                throw Fail.Rule(ErrorCodes.JournalUnbalancedWithTax, Money.Format(totalDebit), Money.Format(taxDebit), Money.Format(totalCredit), Money.Format(taxCredit));
            throw Fail.Rule(ErrorCodes.JournalUnbalanced, Money.Format(totalDebit), Money.Format(totalCredit));
        }
        if (totalDebit > Money.MaxAmount) throw Fail.Field("lines", ErrorCodes.ValidationRange, new FieldRef("lines")); // entry totals are DECIMAL(18,3)

        // The number lock serialises posting per company; the period is checked again after it, so a period closed
        // concurrently (PeriodService takes the same lock) is always seen (BR-036).
        await periods.EnsureOpenAsync(request.EntryDate, ct).ConfigureAwait(false);
        var number = await numbering.NextAsync(DocumentTypes.JournalEntry, ct).ConfigureAwait(false);
        await periods.EnsureOpenAsync(request.EntryDate, ct).ConfigureAwait(false);

        var entry = new JournalEntry
        {
            Number = number,
            EntryDate = request.EntryDate,
            PeriodMonth = AccountingCodes.Month(request.EntryDate),
            Description = description!,
            Reference = TextNormalizer.CleanText(request.Reference),
            SourceModule = request.SourceModule,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            IsManual = request.IsManual,
            Status = AccountingCodes.Posted,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
        };
        var lastLineNo = resolved.Count;
        var taxLines = new List<EntryLine>();
        for (var i = 0; i < resolved.Count; i++)
        {
            var l = resolved[i];
            var tax = taxes[i];
            var line = new EntryLine
            {
                LineNo = i + 1,
                AccountId = l.AccountId!.Value,
                Debit = l.Debit,
                Credit = l.Credit,
                CostCentreId = l.CostCentreId,
                PartyModule = l.Party?.Module,
                PartyType = l.Party?.Type,
                PartyId = l.Party?.Id,
                Note = TextNormalizer.CleanText(l.Note),
                Reference = TextNormalizer.CleanText(l.Reference) ?? reference,
                TaxCodeId = tax?.Code.Id,
                TaxRate = tax?.Code.Rate,
                TaxAmount = tax?.Amount,
                TaxAccountId = tax?.AccountId,
            };
            entry.Lines.Add(line);
            // The automatic tax line: same side, cost centre and reference, linked to its line; numbered after the entered lines.
            if (tax is { AccountId: long taxAccount, Amount: > 0 } t)
            {
                taxLines.Add(new EntryLine
                {
                    LineNo = ++lastLineNo,
                    AccountId = taxAccount,
                    Debit = l.Debit > 0 ? t.Amount : 0,
                    Credit = l.Debit > 0 ? 0 : t.Amount,
                    CostCentreId = l.CostCentreId,
                    Note = Truncate(JournalTax.Note(t.Code.NameAr, t.Code.NameEn, t.Code.Code, i + 1), NoteMax),
                    Reference = line.Reference,
                    TaxCodeId = t.Code.Id,
                    TaxRate = t.Code.Rate,
                    TaxForLine = line,
                });
            }
        }
        foreach (var t in taxLines) entry.Lines.Add(t);
        db.Set<JournalEntry>().Add(entry);
        changes.SetAction(entry, auditAction, auditReason);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entry;
    }

    /// <summary>
    /// Posts the entry of a financial document (FR-096). A technical failure is rolled back to a savepoint and the
    /// entry waits in Pending postings (FR-101); the method then returns null and the document is still saved.
    /// </summary>
    public async Task<JournalEntry?> PostOrPendAsync(PostingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tx = RequireTransaction();
        await tx.CreateSavepointAsync(Savepoint, ct).ConfigureAwait(false);
        var trackedBefore = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        try
        {
            var entry = await PostAsync(request, ct).ConfigureAwait(false);
            await tx.ReleaseSavepointAsync(Savepoint, ct).ConfigureAwait(false);
            return entry;
        }
        catch (Exception ex) when (ex is not AppException and not OperationCanceledException && request.SourceId is not null && request.SourceType is not null)
        {
            try
            {
                await tx.RollbackToSavepointAsync(Savepoint, ct).ConfigureAwait(false);
            }
            catch (Exception rollbackFailure) when (rollbackFailure is not OperationCanceledException)
            {
                // The failure doomed the whole transaction (deadlock victim, broken connection): nothing can be kept,
                // so the operation fails with the original error (CR-B2-02).
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
            }
            // Forget everything tracked since the savepoint (entry, lines, audit rows), saved or not (CR-B2-04).
            // Lines before their entry: detaching a principal first would sever the required relationship.
            foreach (var e in db.ChangeTracker.Entries().Where(e => !trackedBefore.Contains(e.Entity))
                         .OrderBy(e => e.Entity is EntryLine ? 0 : e.Entity is JournalEntry ? 2 : 1).ToList())
                e.State = EntityState.Detached;
            var now = clock.GetUtcNow().UtcDateTime;
            var reason = TechnicalReason(ex, log);
            // One open pending row per document (UX_PendingPostings_Source): a repeated failure updates it (CR-B2-03).
            var open = await db.Set<PendingPosting>().SingleOrDefaultAsync(p => p.SourceModule == request.SourceModule
                && p.SourceType == request.SourceType && p.SourceId == request.SourceId && p.ResolvedAt == null, ct).ConfigureAwait(false);
            var first = open is null;
            if (open is null)
            {
                db.Set<PendingPosting>().Add(new PendingPosting
                {
                    SourceModule = request.SourceModule,
                    SourceType = request.SourceType,
                    SourceId = request.SourceId.Value,
                    Reason = reason,
                    Attempts = 1,
                    LastAttemptAt = now,
                });
            }
            else
            {
                open.Reason = reason;
                open.Attempts++;
                open.LastAttemptAt = now;
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (first && notifications is not null && ctx.CompanyId is long companyId)
            {
                // FR-190: the users who retry postings (je.export) learn that an entry waits.
                await notifications.NotifyAsync(await notifications.HoldersAsync(companyId, Contracts.Security.PermissionKeys.JeExport, ct).ConfigureAwait(false), companyId,
                    Collaboration.TransportationNotificationTypes.PostingFailed, new Dictionary<string, string?> { ["description"] = request.Description }, "/ledger/pending-postings", ct)
                    .ConfigureAwait(false);
            }
            return null;
        }
    }

    /// <summary>
    /// Posts the mirror of <paramref name="original"/>, dated like the original so the void or edit nets to zero in the
    /// original's period (DT-22), and links both ways. With <paramref name="voidReason"/> the original becomes Void
    /// (manual entries, FR-113); otherwise Reversed (edits and document voids, FR-099).
    /// </summary>
    public async Task<JournalEntry> ReverseAsync(JournalEntry original, string description, string? voidReason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(original);
        RequireTransaction();
        if (original.Status != AccountingCodes.Posted) throw Fail.Rule(ErrorCodes.JournalNotPosted, original.Number);
        var lines = await db.Set<EntryLine>().AsNoTracking().Where(l => l.JournalEntryId == original.Id).OrderBy(l => l.LineNo)
            .ToListAsync(ct).ConfigureAwait(false);

        await periods.EnsureOpenAsync(original.EntryDate, ct).ConfigureAwait(false);
        var number = await numbering.NextAsync(DocumentTypes.JournalEntry, ct).ConfigureAwait(false);
        await periods.EnsureOpenAsync(original.EntryDate, ct).ConfigureAwait(false);

        var reversal = new JournalEntry
        {
            Number = number,
            EntryDate = original.EntryDate,
            PeriodMonth = original.PeriodMonth,
            Description = Truncate(description, DescriptionMax),
            Reference = original.Reference,
            SourceModule = original.SourceModule,
            SourceType = original.SourceType,
            SourceId = original.SourceId,
            IsManual = original.IsManual,
            Status = AccountingCodes.Posted,
            ReversesEntryId = original.Id,
            TotalDebit = original.TotalCredit,
            TotalCredit = original.TotalDebit,
        };
        // Line references and tax (DT-254, DT-255) are mirrored too; an automatic tax line stays linked to the mirror of its line.
        var mirrors = new Dictionary<long, EntryLine>();
        foreach (var l in lines)
        {
            var mirror = new EntryLine
            {
                LineNo = l.LineNo,
                AccountId = l.AccountId,
                Debit = l.Credit,
                Credit = l.Debit,
                CostCentreId = l.CostCentreId,
                PartyModule = l.PartyModule,
                PartyType = l.PartyType,
                PartyId = l.PartyId,
                Note = l.Note,
                Reference = l.Reference,
                TaxCodeId = l.TaxCodeId,
                TaxRate = l.TaxRate,
                TaxAmount = l.TaxAmount,
                TaxAccountId = l.TaxAccountId,
                TaxForLine = l.TaxForLineId is long source ? mirrors.GetValueOrDefault(source) : null,
            };
            mirrors[l.Id] = mirror;
            reversal.Lines.Add(mirror);
        }
        db.Set<JournalEntry>().Add(reversal);
        changes.SetAction(reversal, "Reversal");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        original.ReversedByEntryId = reversal.Id;
        if (voidReason is null)
        {
            original.Status = AccountingCodes.Reversed;
            changes.SetAction(original, "Reverse", reversal.Number);
        }
        else
        {
            original.Status = AccountingCodes.Void;
            original.VoidReason = voidReason;
            original.VoidedBy = ctx.UserId ?? throw new InvalidOperationException("A void needs an acting user.");
            original.VoidedAt = clock.GetUtcNow().UtcDateTime;
            changes.SetAction(original, "Void", voidReason);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return reversal;
    }

    // ------------------------------------------------------------ helpers

    private sealed record TaxAccountRow(long Id, string Code, string Status, bool IsPostable, bool IsTaxAccount, bool IsControlAccount, bool HasChildren);

    /// <summary>
    /// The tax of line <paramref name="i"/> (DT-255): an active code; amount × rate rounded to the currency; for a rate above 0 % the tax account
    /// the user chose (never a default), which must be an active postable tax account. A 0 % code needs no tax account and makes no tax line.
    /// </summary>
    private static (TaxCode Code, decimal Amount, long? AccountId) TaxOf(PostingLine l, int i, long taxCodeId, Dictionary<long, TaxCode> codes,
        Dictionary<long, TaxAccountRow> accounts, int decimals)
    {
        var field = $"lines[{i}]";
        var lineNo = i + 1;
        if (!codes.TryGetValue(taxCodeId, out var code)) throw Fail.Field(field + ".taxCodeId", ErrorCodes.ValidationFormat, new FieldRef(field + ".taxCodeId"));
        if (code.Status != AccountingCodes.Active) throw Fail.Field(field + ".taxCodeId", ErrorCodes.TaxCodeInactive, lineNo, code.Code);
        var amount = JournalTax.Amount(l.Debit + l.Credit, code.Rate, decimals);
        if (code.Rate == 0) return (code, amount, null);
        if (l.TaxAccountId is not long id) throw Fail.Field(field + ".taxAccountId", ErrorCodes.TaxAccountRequired, lineNo, code.Code);
        if (!accounts.TryGetValue(id, out var a) || !a.IsTaxAccount || a.IsControlAccount || a.Status != AccountingCodes.Active || !a.IsPostable || a.HasChildren)
            throw Fail.Field(field + ".taxAccountId", ErrorCodes.TaxAccountInvalid, lineNo, a?.Code ?? "#" + lineNo);
        return (code, amount, id);
    }

    /// <summary>G-17: Customer = TRN customer or ACC party with role Customer; Supplier = ACC party with role Supplier; Driver/Partner = TRN.</summary>
    public static bool PartyMatches(string? controlType, PostingParty party, IReadOnlyDictionary<long, string> apArRoles) => controlType switch
    {
        "Customer" => party is { Module: AccountingCodes.ModuleTrn, Type: AccountingCodes.PartyCustomer }
                      || (party is { Module: AccountingCodes.ModuleAcc, Type: AccountingCodes.PartyApAr } && apArRoles.GetValueOrDefault(party.Id) == "Customer"),
        "Supplier" => party is { Module: AccountingCodes.ModuleAcc, Type: AccountingCodes.PartyApAr } && apArRoles.GetValueOrDefault(party.Id) == "Supplier",
        "Driver" => party is { Module: AccountingCodes.ModuleTrn, Type: AccountingCodes.PartyDriver },
        "Partner" => party is { Module: AccountingCodes.ModuleTrn, Type: AccountingCodes.PartyPartner },
        _ => false,
    };

    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction RequireTransaction() =>
        db.Database.CurrentTransaction ?? throw new InvalidOperationException("Posting must run inside the document's transaction (NFR-04).");

    /// <summary>
    /// The reason stored for users (SR-B2-04): a generic bilingual message with a reference; the exception itself is
    /// logged on the server under that reference and never shown.
    /// </summary>
    internal static string TechnicalReason(Exception ex, ILogger? logger)
    {
        var reference = Guid.NewGuid().ToString("N")[..10];
        if (logger is not null) LogTechnicalFailure(logger, ex, reference);
        return $"خطأ تقني أثناء الترحيل وستُعاد المحاولة. المرجع: {reference} / Technical error while posting; it will be retried. Reference: {reference}";
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Posting failed for a technical reason (reference {Reference}).")]
    private static partial void LogTechnicalFailure(ILogger logger, Exception exception, string reference);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
