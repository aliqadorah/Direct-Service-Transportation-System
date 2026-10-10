using System.Linq.Expressions;
using System.Security.Cryptography;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.EInvoicing;

/// <summary>Wakes the e-invoicing worker when a document is queued, so automatic submission follows approval closely (FR-061).</summary>
public interface IEInvoiceSignal
{
    void Notify();
}

/// <summary>
/// National e-invoicing of invoices and credit notes (FR-060 … FR-065, PRD 9.7, D-04). Approval and credit notes queue a submission
/// with the next invoice counter (ICV) in their own transaction; the worker (or inv.issue, manually) builds the document from current
/// data, validates it (missing or invalid values listed; the document stays Queued), submits it through the company's connector and
/// stores the unique ID, system number, QR and time (Submitted). A refusal is Rejected with the reason and can be resubmitted after
/// correction; technical failures are retried with back-off, then Rejected with the last error ("Failed", D-04). A credit note is
/// submitted only after its invoice was accepted (FR-064). Voids cancel documents not yet accepted (NotRequired).
/// </summary>
public sealed class EInvoiceService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IChangeContext changes, IEInvoiceStore store,
    EInvoiceSettingsService settings, EInvoiceDocumentBuilder builder, ISpreadsheetService sheets, IEInvoiceSignal signal, IOptions<EInvoicingOptions> options,
    TimeProvider clock)
{
    public const string EntityType = "TRN.EInvoiceSubmission";
    private const string ValidationPrefix = "validation:";
    /// <summary>LastError of an attempt whose outcome is unknown (technical failure, or "in-flight" while the call runs).</summary>
    private const string TechnicalPrefix = "technical:";
    private const string InFlight = "technical:in-flight";

    private bool En => ctx.Language == "en";

    // ============================================================ queue and cancel (called inside the document's transaction)

    /// <summary>Queues an approved invoice when e-invoicing is enabled (FR-043): Queued with the next ICV.</summary>
    internal Task QueueInvoiceAsync(Invoice inv, CancellationToken ct) => QueueAsync(EInvoiceCodes.Invoice, inv.Id, null, ct);

    /// <summary>
    /// Queues a new credit note when e-invoicing is enabled and its invoice is itself e-invoiced (queued, rejected or accepted); it is
    /// sent once the invoice is accepted (FR-064). Invoices approved before e-invoicing was switched on have no submission, and neither
    /// have their credit notes (review H2).
    /// </summary>
    internal async Task QueueCreditNoteAsync(CreditNote cn, CancellationToken ct)
    {
        if (!await db.Set<EInvoiceSubmission>().AnyAsync(s => s.InvoiceId == cn.InvoiceId && s.Status != EInvoiceCodes.NotRequired, ct).ConfigureAwait(false)) return;
        await QueueAsync(EInvoiceCodes.CreditNote, null, cn.Id, ct).ConfigureAwait(false);
    }

    private async Task QueueAsync(string type, long? invoiceId, long? creditNoteId, CancellationToken ct)
    {
        if (await settings.EnabledAsync(ct).ConfigureAwait(false) is null) return;
        var icv = await store.NextIcvAsync(ct).ConfigureAwait(false);
        db.Set<EInvoiceSubmission>().Add(new EInvoiceSubmission { DocumentType = type, InvoiceId = invoiceId, CreditNoteId = creditNoteId, Status = EInvoiceCodes.Queued, Icv = icv });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        signal.Notify();
    }

    /// <summary>
    /// On void: refuses (false) when the national system accepted the document or may have received it (an attempt in flight or ended
    /// by a technical failure — resubmit first to learn the outcome, review H1); otherwise its queued or rejected submission becomes
    /// NotRequired with the reason. Waits for a claim in progress (short transactions only).
    /// </summary>
    internal async Task<bool> CancelAsync(long? invoiceId, long? creditNoteId, string reason, CancellationToken ct)
    {
        await store.LockDocumentAsync(invoiceId, creditNoteId, ct).ConfigureAwait(false);
        var rows = await db.Set<EInvoiceSubmission>().Where(s => (invoiceId != null && s.InvoiceId == invoiceId) || (creditNoteId != null && s.CreditNoteId == creditNoteId))
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var r in rows) await db.Set<EInvoiceSubmission>().Entry(r).ReloadAsync(ct).ConfigureAwait(false);
        if (rows.Any(r => r.Status == EInvoiceCodes.Submitted)) return false;
        if (rows.Any(r => r.Status is EInvoiceCodes.Queued or EInvoiceCodes.Rejected && OutcomeUnknown(r.LastError)))
            throw Fail.Rule(ErrorCodes.EInvoiceOutcomeUnknown);
        foreach (var r in rows.Where(r => r.Status is EInvoiceCodes.Queued or EInvoiceCodes.Rejected))
        {
            r.Status = EInvoiceCodes.NotRequired;
            r.LastError = Truncate(reason);
            changes.SetAction(r, "Cancel", reason);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>A request may have reached the national system without an answer (in flight, timeout, server error).</summary>
    private static bool OutcomeUnknown(string? lastError) => lastError is not null && lastError.StartsWith(TechnicalPrefix, StringComparison.Ordinal);

    // ============================================================ submission (worker and manual)

    /// <summary>Manual submission or resubmission by inv.issue (FR-061, FR-063): processed now; the result is returned.</summary>
    public async Task<EInvoiceSubmissionDetails> SubmitAsync(string token, CancellationToken ct)
    {
        var s = await support.FindAsync<EInvoiceSubmission>(token, ct).ConfigureAwait(false);
        await ProcessAsync(s.Id, manual: true, ct).ConfigureAwait(false);
        return await DetailsAsync(s.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Queued submissions of the current company due for an automatic attempt, decided in SQL (review M1): never tried first, then
    /// technical failures after their back-off (2, 4, 8 … ≤ 60 min) and documents with problems every 10 minutes, oldest first.
    /// </summary>
    public async Task<IReadOnlyList<long>> DueAsync(int max, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var problems = now.AddMinutes(-ProblemRecheckMinutes);
        DateTime B(int minutes) => now.AddMinutes(-minutes);
        DateTime b2 = B(2), b4 = B(4), b8 = B(8), b16 = B(16), b32 = B(32), b60 = B(60), stale = now.AddMinutes(-InFlightMinutes);
        return await db.Set<EInvoiceSubmission>().AsNoTracking()
            .Where(s => s.Status == EInvoiceCodes.Queued)
            .Select(s => new { s.Id, s.LastError, s.Attempts, At = s.UpdatedAt ?? s.CreatedAt })
            .Where(s => s.LastError == null
                || (s.LastError.StartsWith(ValidationPrefix) && s.At <= problems)
                || (s.LastError == InFlight && s.At <= stale)
                || (s.LastError.StartsWith(TechnicalPrefix) && s.LastError != InFlight
                    && ((s.Attempts <= 1 && s.At <= b2) || (s.Attempts == 2 && s.At <= b4) || (s.Attempts == 3 && s.At <= b8)
                        || (s.Attempts == 4 && s.At <= b16) || (s.Attempts == 5 && s.At <= b32) || (s.Attempts >= 6 && s.At <= b60))))
            .OrderBy(s => s.LastError == null ? 0 : 1).ThenBy(s => s.At).ThenBy(s => s.Id)
            .Take(max).Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
    }

    private const int ProblemRecheckMinutes = 10;
    private const int InFlightMinutes = 5;

    /// <summary>2, 4, 8 … minutes after each technical failure, at most hourly.</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Clamp(attempts, 1, 6))));

    private sealed record Prepared(EInvoiceDocument Document, string Payload, IEInvoiceConnector Connector, EInvoiceCredentials Credentials);

    /// <summary>
    /// Processes one submission; true when the document was accepted. Three steps so no database transaction is held during the
    /// call (review M3): (1) claim the row (UPDLOCK + READPAST), validate, build, mark it in flight and count the attempt — committed;
    /// (2) call the national system; (3) store the outcome. A crash between (1) and (3) leaves the row in flight: it is retried after
    /// 5 minutes with the same UUID (the national system answers ALREADY_SUBMITTED if it has it) and cannot be voided meanwhile.
    /// </summary>
    public async Task<bool> ProcessAsync(long id, bool manual, CancellationToken ct)
    {
        Prepared? prepared = null;
        await db.InTransactionAsync(async c =>
        {
            if (!await store.TryClaimAsync(id, c).ConfigureAwait(false))
            {
                if (manual) throw Fail.Conflict(ErrorCodes.EInvoiceBusy);
                return;
            }
            var s = await db.Set<EInvoiceSubmission>().SingleAsync(x => x.Id == id, c).ConfigureAwait(false);
            await db.Set<EInvoiceSubmission>().Entry(s).ReloadAsync(c).ConfigureAwait(false);
            if (s.Status is EInvoiceCodes.Submitted or EInvoiceCodes.NotRequired)
            {
                if (manual) throw Fail.Rule(ErrorCodes.EInvoiceNotSubmittable, s.Status);
                return;
            }
            if (s.LastError == InFlight && (s.UpdatedAt ?? s.CreatedAt) > clock.GetUtcNow().UtcDateTime.AddMinutes(-InFlightMinutes))
            {
                if (manual) throw Fail.Conflict(ErrorCodes.EInvoiceBusy);
                return;
            }
            if (s.Status == EInvoiceCodes.Rejected)
            {
                if (!manual) return;
                // Correct and resubmit (D-04): the document is rebuilt from current data with the same UUID and counter.
                s.Status = EInvoiceCodes.Queued;
                s.Attempts = 0;
                changes.SetAction(s, "Resubmit", null);
            }

            var problems = new List<EInvoiceProblem>();
            var config = await settings.EnabledAsync(c).ConfigureAwait(false);
            var connector = config is null ? null : settings.Connector(config.Connector);
            EInvoiceDocument? doc = null;
            EInvoiceCredentials? credentials = null;
            if (config is null) problems.Add(new("settings", ErrorCodes.EInvoiceNotEnabled));
            else if (connector is null) problems.Add(new("connector", ErrorCodes.EInvoiceConnectorUnavailable));
            else
            {
                doc = await DocumentAsync(s, config, c).ConfigureAwait(false);
                if (doc is null) problems.Add(new("invoice", ErrorCodes.EInvoiceWaitingForInvoice));
                else problems.AddRange([.. EInvoiceDocumentBuilder.Check(doc, config), .. connector.Validate(doc)]);
                try
                {
                    if (problems.Count == 0) credentials = settings.Credentials(config);
                }
                catch (CryptographicException)
                {
                    // Keys lost or rotated: the credentials must be entered again (review M5).
                    problems.Add(new("credentials", ErrorCodes.EInvoiceCredentials));
                }
            }
            if (problems.Count > 0)
            {
                // FR-060 / 9.7: stays Queued with the values to fix; re-checked every 10 minutes or on manual submission.
                var text = Truncate(ValidationPrefix + string.Join(';', problems.Select(p => $"{p.Field}={p.Code}").Distinct(StringComparer.Ordinal)));
                if (s.LastError != text) s.LastError = text;
                await db.SaveChangesAsync(c).ConfigureAwait(false);
                if (s.LastError == text) await TouchAsync(s.Id, c).ConfigureAwait(false);
                return;
            }

            var payload = connector!.Build(doc!);
            s.RequestPayload = payload;
            s.Attempts++;
            s.LastError = InFlight;
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            prepared = new Prepared(doc!, payload, connector, credentials!);
        }, ct).ConfigureAwait(false);
        if (prepared is null) return false;

        EInvoiceOutcome outcome;
        try
        {
            outcome = await prepared.Connector.SubmitAsync(prepared.Payload, prepared.Credentials, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or FormatException && !ct.IsCancellationRequested)
        {
            outcome = new EInvoiceOutcome(EInvoiceOutcomeKind.Failed, null, null, null, ex.Message, null);
        }

        var accepted = false;
        await db.InTransactionAsync(async c =>
        {
            await store.LockAsync(id, c).ConfigureAwait(false);
            var s = await db.Set<EInvoiceSubmission>().SingleAsync(x => x.Id == id, c).ConfigureAwait(false);
            await db.Set<EInvoiceSubmission>().Entry(s).ReloadAsync(c).ConfigureAwait(false);
            s.ResponsePayload = outcome.ResponsePayload;
            switch (outcome.Kind)
            {
                case EInvoiceOutcomeKind.Accepted:
                    s.Status = EInvoiceCodes.Submitted;
                    s.UniqueId = outcome.UniqueId ?? s.UniqueId ?? prepared.Document.Uuid;
                    // ALREADY_SUBMITTED may come without identifiers: keep what an earlier answer gave (review L1).
                    s.SystemNumber = outcome.SystemNumber ?? s.SystemNumber;
                    s.QrCode = outcome.QrCode ?? s.QrCode;
                    s.SubmittedAt ??= clock.GetUtcNow().UtcDateTime;
                    s.LastError = null;
                    changes.SetAction(s, "Accepted", s.SystemNumber);
                    accepted = true;
                    break;
                case EInvoiceOutcomeKind.Rejected:
                    s.Status = EInvoiceCodes.Rejected;
                    s.LastError = Truncate(outcome.Message);
                    changes.SetAction(s, "Rejected", s.LastError);
                    break;
                default:
                    // The request may have arrived: kept as "outcome unknown" until a later attempt answers (review H1).
                    s.LastError = Truncate(TechnicalPrefix + outcome.Message);
                    if (s.Attempts >= options.Value.MaxAttempts)
                    {
                        s.Status = EInvoiceCodes.Rejected;
                        changes.SetAction(s, "Failed", outcome.Message);
                    }
                    break;
            }
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            if (accepted && s.InvoiceId is long invoiceId)
            {
                // Credit notes waiting for this invoice become due at once.
                await db.Set<EInvoiceSubmission>().Where(x => x.CreditNoteId != null && x.CreditNote!.InvoiceId == invoiceId && x.Status == EInvoiceCodes.Queued)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, DateTime.UnixEpoch), c).ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
        if (accepted) signal.Notify();
        return accepted;
    }

    /// <summary>Moves the re-check time of a document with unchanged problems (technical column only; not an audited change).</summary>
    private Task<int> TouchAsync(long id, CancellationToken ct) =>
        db.Set<EInvoiceSubmission>().Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, clock.GetUtcNow().UtcDateTime), ct);

    /// <summary>The document of a submission, or null for a credit note whose invoice is not accepted yet (FR-064).</summary>
    private async Task<EInvoiceDocument?> DocumentAsync(EInvoiceSubmission s, EInvoiceSettings config, CancellationToken ct)
    {
        if (s.InvoiceId is long invoiceId)
        {
            var inv = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct).ConfigureAwait(false);
            return await builder.InvoiceAsync(inv, s.Icv ?? 0, config, ct).ConfigureAwait(false);
        }
        var cn = await db.Set<CreditNote>().AsNoTracking().SingleAsync(x => x.Id == s.CreditNoteId, ct).ConfigureAwait(false);
        var invoice = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == cn.InvoiceId, ct).ConfigureAwait(false);
        var original = await db.Set<EInvoiceSubmission>().AsNoTracking().Where(x => x.InvoiceId == invoice.Id && x.Status == EInvoiceCodes.Submitted)
            .Select(x => new { x.UniqueId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (original is null) return null;
        return await builder.CreditNoteAsync(cn, invoice, original.UniqueId ?? invoice.PublicId, s.Icv ?? 0, config, ct).ConfigureAwait(false);
    }

    private static string? Truncate(string? text) => text is null ? null : text.Length <= 1000 ? text : text[..1000];

    // ============================================================ reads (FR-061, FR-063, FR-065)

    private sealed record Row(EInvoiceSubmission S, Guid DocPid, string? Number, DateOnly Date, decimal Total, Guid CustomerPid, string CustomerName, Guid? OriginalPid, string? OriginalNumber);

    private static IQueryable<Row> Rows(IQueryable<EInvoiceSubmission> q) => q.Select(s => new Row(s,
        s.InvoiceId != null ? s.Invoice!.PublicId : s.CreditNote!.PublicId,
        s.InvoiceId != null ? s.Invoice!.Number : s.CreditNote!.Number,
        s.InvoiceId != null ? s.Invoice!.InvoiceDate : s.CreditNote!.CreditDate,
        s.InvoiceId != null ? s.Invoice!.Total : s.CreditNote!.Total,
        s.InvoiceId != null ? s.Invoice!.Customer.PublicId : s.CreditNote!.Customer.PublicId,
        s.InvoiceId != null ? s.Invoice!.Customer.Name : s.CreditNote!.Customer.Name,
        s.CreditNoteId != null ? s.CreditNote!.Invoice.PublicId : null,
        s.CreditNoteId != null ? s.CreditNote!.Invoice.Number : null));

    private static readonly IReadOnlyDictionary<string, Expression<Func<EInvoiceSubmission, object?>>> SortFields =
        new Dictionary<string, Expression<Func<EInvoiceSubmission, object?>>>(StringComparer.Ordinal)
        {
            ["date"] = s => s.InvoiceId != null ? s.Invoice!.InvoiceDate : s.CreditNote!.CreditDate,
            ["icv"] = s => s.Icv,
            ["status"] = s => s.Status,
            ["submittedAt"] = s => s.SubmittedAt,
            ["total"] = s => s.InvoiceId != null ? s.Invoice!.Total : s.CreditNote!.Total,
        };

    private IQueryable<EInvoiceSubmission> Filter(EInvoiceListQuery q, bool withStatus)
    {
        var query = db.Set<EInvoiceSubmission>().AsNoTracking();
        if (withStatus)
        {
            query = (q.Status?.Trim().ToLowerInvariant()) switch
            {
                null or "" => query,
                "queued" => query.Where(s => s.Status == EInvoiceCodes.Queued),
                "submitted" => query.Where(s => s.Status == EInvoiceCodes.Submitted),
                "rejected" => query.Where(s => s.Status == EInvoiceCodes.Rejected),
                "notrequired" => query.Where(s => s.Status == EInvoiceCodes.NotRequired),
                // The Rejected/Failed list (FR-061): what needs attention.
                "attention" => query.Where(s => s.Status == EInvoiceCodes.Rejected || (s.Status == EInvoiceCodes.Queued && s.LastError != null)),
                _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, "status"),
            };
        }
        query = (q.DocumentType?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "invoice" => query.Where(s => s.DocumentType == EInvoiceCodes.Invoice),
            "creditnote" => query.Where(s => s.DocumentType == EInvoiceCodes.CreditNote),
            _ => throw Fail.Field("documentType", ErrorCodes.ValidationFormat, "documentType"),
        };
        if (!string.IsNullOrWhiteSpace(q.Customer))
        {
            var g = support.Decode<Customer>(q.Customer) ?? throw Fail.Field("customer", ErrorCodes.ValidationFormat, "customer");
            query = query.Where(s => s.InvoiceId != null ? s.Invoice!.Customer.PublicId == g : s.CreditNote!.Customer.PublicId == g);
        }
        if (q.From is DateOnly from) query = query.Where(s => (s.InvoiceId != null ? s.Invoice!.InvoiceDate : s.CreditNote!.CreditDate) >= from);
        if (q.To is DateOnly to) query = query.Where(s => (s.InvoiceId != null ? s.Invoice!.InvoiceDate : s.CreditNote!.CreditDate) <= to);
        if (MasterDataSupport.SearchText(q.Q) is { } text)
            query = query.Where(s => (s.InvoiceId != null ? s.Invoice!.Number!.Contains(text) : s.CreditNote!.Number.Contains(text)) || (s.SystemNumber != null && s.SystemNumber.Contains(text)));
        return query;
    }

    public async Task<EInvoicePage> ListAsync(EInvoiceListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = Filter(q, withStatus: true);
        var paging = new PageRequest(q.Page, q.PageSize);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await Rows(MasterDataSupport.Sort(query, q.Sort, SortFields, "-date").ThenByDescending(s => s.Id).Skip(paging.Skip).Take(paging.SafePageSize))
            .ToListAsync(ct).ConfigureAwait(false);
        // Totals by status over the period and other filters (FR-065).
        var totals = await Filter(q, withStatus: false)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(s => s.InvoiceId != null ? s.Invoice!.Total : s.CreditNote!.Total) })
            .ToListAsync(ct).ConfigureAwait(false);
        return new EInvoicePage(new PagedResult<EInvoiceSubmissionResponse>(rows.Select(ToResponse).ToList(), paging.SafePage, paging.SafePageSize, total),
            totals.OrderBy(t => t.Status, StringComparer.Ordinal).Select(t => new EInvoiceStatusTotal(t.Status, t.Count, t.Total)).ToList());
    }

    /// <summary>The e-invoicing report as Excel or CSV (FR-065).</summary>
    public async Task<GeneratedFile> ExportAsync(EInvoiceListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var rows = await Rows(MasterDataSupport.Sort(Filter(q, withStatus: true), q.Sort, SortFields, "-date").ThenByDescending(s => s.Id).Take(SheetData.MaxRows))
            .ToListAsync(ct).ConfigureAwait(false);
        string[] headers = En
            ? ["Document", "Number", "Date", "Customer", "Total", "Status", "ICV", "System number", "Unique ID", "Submitted at", "Attempts", "Last error"]
            : ["المستند", "الرقم", "التاريخ", "العميل", "الإجمالي", "الحالة", "عداد الفواتير", "رقم النظام", "المعرّف الفريد", "وقت القبول", "المحاولات", "آخر خطأ"];
        var data = rows.Select(r => (IReadOnlyList<object?>)[
            r.S.DocumentType, r.Number, r.Date, r.CustomerName, r.Total, r.S.Status, r.S.Icv, r.S.SystemNumber, r.S.UniqueId?.ToString(), r.S.SubmittedAt,
            r.S.Attempts, Problems(r.S.LastError) is { Count: > 0 } p ? string.Join(" | ", p.Select(x => x.Message)) : Display(r.S.LastError)]).ToList();
        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? new GeneratedFile("e-invoicing.csv", "text/csv", sheets.WriteCsv(headers, data))
            : new GeneratedFile("e-invoicing.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sheets.WriteXlsx("e-invoicing", headers, data));
    }

    public async Task<EInvoiceSubmissionDetails> GetAsync(string token, CancellationToken ct)
    {
        var s = await support.FindAsync<EInvoiceSubmission>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(s.Id, ct).ConfigureAwait(false);
    }

    /// <summary>The submission of an invoice or credit note (invoice and credit-note views, FR-048), or 404 when none.</summary>
    public async Task<EInvoiceSubmissionDetails> ForDocumentAsync(long? invoiceId, long? creditNoteId, CancellationToken ct)
    {
        var id = await db.Set<EInvoiceSubmission>().AsNoTracking().Where(s => (invoiceId != null && s.InvoiceId == invoiceId) || (creditNoteId != null && s.CreditNoteId == creditNoteId))
            .OrderByDescending(s => s.Id).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        return await DetailsAsync(id, ct).ConfigureAwait(false);
    }

    internal async Task<EInvoiceSubmissionDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var row = await Rows(db.Set<EInvoiceSubmission>().AsNoTracking().Where(s => s.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        return new EInvoiceSubmissionDetails(ToResponse(row), row.S.RequestPayload, row.S.ResponsePayload,
            row.OriginalPid is Guid g ? new DocRef(support.Token<Invoice>(g), row.OriginalNumber) : null);
    }

    private EInvoiceSubmissionResponse ToResponse(Row r)
    {
        var problems = Problems(r.S.LastError);
        var document = r.S.InvoiceId != null ? new DocRef(support.Token<Invoice>(r.DocPid), r.Number) : new DocRef(support.Token<CreditNote>(r.DocPid), r.Number);
        return new EInvoiceSubmissionResponse(support.Token<EInvoiceSubmission>(r.S.PublicId), r.S.DocumentType, document, new Ref(support.Token<Customer>(r.CustomerPid), r.CustomerName),
            r.Date, r.Total, r.S.Status, r.S.Icv, r.S.UniqueId, r.S.SystemNumber, !string.IsNullOrEmpty(r.S.QrCode), r.S.Attempts,
            problems.Count > 0 ? null : Display(r.S.LastError), problems, problems.Any(p => p.Code == ErrorCodes.EInvoiceWaitingForInvoice),
            r.S.SubmittedAt, r.S.CreatedAt, r.S.UpdatedAt, Convert.ToBase64String(r.S.RowVersion));
    }

    /// <summary>The last error as shown: technical failures without their marker; an attempt in flight as "being submitted".</summary>
    private string? Display(string? lastError) => lastError switch
    {
        InFlight => ErrorCatalog.Format(ErrorCodes.EInvoiceBusy, ctx.Language),
        { } t when t.StartsWith(TechnicalPrefix, StringComparison.Ordinal) => t[TechnicalPrefix.Length..],
        _ => lastError,
    };

    /// <summary>Validation problems stored as "validation:field=code;…", shown in the reader's language.</summary>
    private List<EInvoiceProblemResponse> Problems(string? lastError)
    {
        if (lastError is null || !lastError.StartsWith(ValidationPrefix, StringComparison.Ordinal)) return [];
        return lastError[ValidationPrefix.Length..].Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => new EInvoiceProblemResponse(p[0], p[1], ErrorCatalog.IsKnown(p[1]) ? ErrorCatalog.Format(p[1], ctx.Language) : p[1]))
            .ToList();
    }
}
