using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Cash;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Accounting.Domain.Acc;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.ApAr;

/// <summary>
/// Supplier bills (FR-123) and receivable documents (FR-124): party of the kind's role (receivables also for transport customers through
/// their accounting party), date (not future, open period), due date (date + payment terms by default, never before the date), reference,
/// lines with expense/asset (bills) or revenue (receivables) accounts, tax per line rate, totals; numbered BIL- / ARD- inside the
/// transaction and posted in it — bill: Dr lines + sales tax input, Cr the supplier's control account; receivable: Dr the customer's
/// control account, Cr lines + sales tax output. Receivables are queued for e-invoicing when it is on (D-15). Void by reversal with a
/// reason, refused with active payments/returns, in a closed period or once accepted by the e-invoicing system (FR-127). Paid, returned
/// and remaining amounts are maintained by payments and returns (FR-125, FR-126).
/// </summary>
public sealed class ApArDocumentService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, CashSupport cash, IChangeContext changes,
    IRecordLockGuard locks, IPeriodGuard periods, INumberingService numbering, PostingEngine posting, ChartOfAccountsService chart, ApArPartyService parties,
    EInvoiceService einvoice, ISpreadsheetService sheets, IReportExporter exporter)
{
    public const string EntityType = "ACC.ApArDocument";
    public const int MaxLines = 200;

    private bool En => ctx.Language == "en";

    public async Task<(ApArDocumentDetails Value, bool Created)> CreateAsync(string kind, ApArDocumentRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var k = ApArRules.Kind(kind);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<ApArDocument>(clientId, ct).ConfigureAwait(false) is { } again)
            return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);

        var reference = TextNormalizer.CleanText(TextNormalizer.ToAsciiDigits(r.Reference));
        new Validator().MaxLength("reference", reference, 100)
            .Check(r.Lines is { Count: > 0 }, "lines", ErrorCodes.ValidationRequired)
            .Check(r.Lines is null || r.Lines.Count <= MaxLines, "lines", ErrorCodes.ValidationRange)
            .Check(k == ApArCodes.Receivable || string.IsNullOrWhiteSpace(r.CustomerId), "customerId", ErrorCodes.ValidationFormat)
            .Check(!string.IsNullOrWhiteSpace(r.PartyId) || !string.IsNullOrWhiteSpace(r.CustomerId), "partyId", ErrorCodes.ValidationRequired)
            .Check(string.IsNullOrWhiteSpace(r.PartyId) || string.IsNullOrWhiteSpace(r.CustomerId), "customerId", ErrorCodes.ValidationFormat)
            .ThrowIfInvalid();
        var company = await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == ctx.CompanyId, ct).ConfigureAwait(false);
        int decimals = company.CurrencyDecimals;
        var date = await cash.DateAsync(r.DocumentDate, "documentDate", ct).ConfigureAwait(false);

        // Party: an active party of the kind's role; or (receivables) an active transport customer.
        ApArParty? party = null;
        Customer? customer = null;
        if (!string.IsNullOrWhiteSpace(r.PartyId))
        {
            party = await support.ChooseAsync<ApArParty>("partyId", r.PartyId, null, p => p.Name, ct).ConfigureAwait(false);
            if (party.PartyRole != ApArRules.RoleOf(k)) throw Fail.RuleOn("partyId", ErrorCodes.ApArPartyRole, party.Name, new FieldRef(ApArRules.RoleOf(k)));
        }
        else
        {
            customer = await support.ChooseAsync<Customer>("customerId", r.CustomerId, null, c => c.Name, ct).ConfigureAwait(false);
            if (customer.ApArPartyId is long linked) party = await db.Set<ApArParty>().AsNoTracking().SingleAsync(p => p.Id == linked, ct).ConfigureAwait(false);
        }
        var terms = party?.PaymentTermsDays ?? customer!.PaymentTermsDays;
        var due = r.DueDate ?? date.AddDays(terms);
        if (due < date) throw Fail.Field("dueDate", ErrorCodes.ValidationRange, new FieldRef("dueDate"));

        var lines = await LinesAsync(k, r.Lines!, company.TaxRate, decimals, ct).ConfigureAwait(false);
        var subtotal = lines.Sum(l => l.Amount);
        var tax = ApArRules.Tax(lines.Select(l => (l.Amount, l.TaxRate)), decimals);   // on the full amounts, deductions included (revision 6)
        var deduction = lines.Sum(l => l.Deduction);
        var total = subtotal + tax - deduction;
        if (deduction > 0 && !await db.Set<PostingKeyMapping>().AnyAsync(m => m.PostingKey == PostingKeys.SupplierDeductions, ct).ConfigureAwait(false))
        {
            await NoteDeductionAttemptAsync(ct).ConfigureAwait(false);
            throw Fail.Rule(ErrorCodes.ApArDeductionKeyMissing);
        }
        if (total > Money.MaxAmount) throw Fail.Field("lines", ErrorCodes.ValidationRange, new FieldRef("lines"));
        if (total <= 0) throw Fail.Field("lines", ErrorCodes.ValidationRange, new FieldRef("lines"));

        // The buyer's tax number is sent when known and may be missing (PRD 7.8.2; same rule as transport invoices: DT-86, DT-102 as revised by the owner).

        var doc = new ApArDocument
        {
            DocumentKind = k, DocumentDate = date, DueDate = due, Reference = reference, Subtotal = subtotal, Tax = tax, Deduction = deduction, Total = total,
            PaidAmount = 0, ReturnedAmount = 0, RemainingAmount = total, Status = ApArCodes.Posted,
        };
        foreach (var l in lines) doc.Lines.Add(l);
        if (clientId is Guid id) doc.PublicId = id;
        ApArDocument? raced = null;
        try
        {
            await db.InTransactionAsync(async c =>
            {
                await posting.EnsurePostingReadyAsync(c).ConfigureAwait(false);
                await periods.EnsureOpenAsync(date, c).ConfigureAwait(false);
                doc.Number = await numbering.NextAsync(ApArRules.Sequence(k), c).ConfigureAwait(false);
                party ??= await parties.ForCustomerAsync(customer!, c).ConfigureAwait(false);
                doc.PartyId = party.Id;
                db.Set<ApArDocument>().Add(doc);
                if (await support.SaveCreateAsync<ApArDocument>(clientId, c).ConfigureAwait(false) is { } other) throw new RacedCreate(other);
                var entry = await posting.PostOrPendAsync(BuildEntry(doc, doc.Lines.ToList(), party.ControlAccountId, party.Name), c).ConfigureAwait(false);
                doc.JournalEntryId = entry?.Id;
                await db.SaveChangesAsync(c).ConfigureAwait(false);
                if (k == ApArCodes.Receivable) await einvoice.QueueApArDocumentAsync(doc.Id, c).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (RacedCreate rc)
        {
            db.ChangeTracker.Clear();
            raced = rc.Existing;
        }
        return raced is not null ? (await DetailsAsync(raced.Id, ct).ConfigureAwait(false), false) : (await DetailsAsync(doc.Id, ct).ConfigureAwait(false), true);
    }

    private sealed class RacedCreate(ApArDocument existing) : Exception
    {
        public ApArDocument Existing { get; } = existing;
    }

    /// <summary>Lines: description, an active postable leaf account of the kind (bills: expense or asset; receivables: revenue; never a control account), amount, rate, cost centre.</summary>
    private async Task<List<ApArDocumentLine>> LinesAsync(string kind, IReadOnlyList<ApArLineRequest> requests, decimal companyRate, int decimals, CancellationToken ct)
    {
        var result = new List<ApArDocumentLine>();
        var errors = new List<FieldError>();
        for (var i = 0; i < requests.Count; i++)
        {
            var r = requests[i] ?? new ApArLineRequest(null, null, null, null, null);
            var f = $"lines[{i}]";
            var description = TextNormalizer.CleanText(r.Description);
            if (description is null) errors.Add(new FieldError(f + ".description", ErrorCodes.ValidationRequired, [new FieldRef(f + ".description")]));
            else if (description.Length > 300) errors.Add(new FieldError(f + ".description", ErrorCodes.ValidationRange, [new FieldRef(f + ".description")]));
            if (r.Amount is not decimal amount || amount <= 0 || !Money.IsValidAmount(amount, decimals))
                errors.Add(new FieldError(f + ".amount", ErrorCodes.ValidationRange, [new FieldRef(f + ".amount")]));
            var rate = r.TaxRate ?? companyRate;
            if (rate is < 0 or > 100 || decimal.Round(rate, 2) != rate)
                errors.Add(new FieldError(f + ".taxRate", ErrorCodes.ValidationRange, [new FieldRef(f + ".taxRate")]));
            // Revision 6: a deduction only on supplier-bill lines, 0 ≤ deduction ≤ the line amount.
            if (r.Deduction is decimal ded && ded != 0)
            {
                if (kind != ApArCodes.SupplierBill) errors.Add(new FieldError(f + ".deduction", ErrorCodes.ApArDeductionNotAllowed, []));
                else if (ded < 0 || !Money.IsValidAmount(ded, decimals) || (r.Amount is decimal a && ded > a))
                    errors.Add(new FieldError(f + ".deduction", ErrorCodes.ValidationRange, [new FieldRef(f + ".deduction")]));
            }
            if (string.IsNullOrWhiteSpace(r.AccountId))
                errors.Add(new FieldError(f + ".accountId", ErrorCodes.ValidationRequired, [new FieldRef(f + ".accountId")]));
        }
        if (errors.Count > 0) throw Fail.Fields(errors);

        for (var i = 0; i < requests.Count; i++)
        {
            var r = requests[i];
            var account = await support.ChooseAsync<Account>($"lines[{i}].accountId", r.AccountId, null, a => a.Code, ct).ConfigureAwait(false);
            var hasChildren = await db.Set<Account>().AnyAsync(a => a.ParentId == account.Id, ct).ConfigureAwait(false);
            var typeOk = kind == ApArCodes.SupplierBill ? account.AccountType is "Expense" or "Asset" : account.AccountType == "Revenue";
            if (!typeOk || account.IsControlAccount || !account.IsPostable || hasChildren || account.Status != AccountingCodes.Active)
                throw Fail.RuleOn($"lines[{i}].accountId", ErrorCodes.ApArLineAccount, i + 1, account.Code);
            long? costCentreId = null;
            if (!string.IsNullOrWhiteSpace(r.CostCentreId))
            {
                var cc = await support.ChooseAsync<CostCentre>($"lines[{i}].costCentreId", r.CostCentreId, null, c => c.Code, ct).ConfigureAwait(false);
                if (cc.Status != AccountingCodes.Active) throw Fail.RuleOn($"lines[{i}].costCentreId", ErrorCodes.RecordArchived, cc.Code);
                costCentreId = cc.Id;
            }
            result.Add(new ApArDocumentLine
            {
                Description = TextNormalizer.CleanText(r.Description)!, AccountId = account.Id, Amount = r.Amount!.Value, TaxRate = r.TaxRate ?? companyRate, CostCentreId = costCentreId,
                Deduction = r.Deduction ?? 0,
            });
        }
        return result;
    }

    /// <summary>
    /// The document's entry (FR-123, FR-124): bill — Dr each line account (with its cost centre) and sales tax input, Cr the supplier's
    /// control account with the party (the payable, after deductions) and, for deductions (revision 6), Cr "Supplier deductions";
    /// receivable — Dr the customer's control account with the party, Cr each line and sales tax output.
    /// </summary>
    public PostingRequest BuildEntry(ApArDocument d, IReadOnlyList<ApArDocumentLine> lines, long controlAccountId, string partyName)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(lines);
        var party = new PostingParty(AccountingCodes.ModuleAcc, AccountingCodes.PartyApAr, d.PartyId);
        var bill = d.DocumentKind == ApArCodes.SupplierBill;
        var result = new List<PostingLine>();
        if (!bill) result.Add(new PostingLine(null, controlAccountId, d.Total, 0, null, party));
        foreach (var l in lines)
            result.Add(bill ? new PostingLine(null, l.AccountId, l.Amount, 0, l.CostCentreId, null, Note(l.Description)) : new PostingLine(null, l.AccountId, 0, l.Amount, l.CostCentreId, null, Note(l.Description)));
        if (d.Tax > 0) result.Add(bill ? PostingLine.DebitKey(PostingKeys.SalesTaxInput, d.Tax) : PostingLine.CreditKey(PostingKeys.SalesTaxOutput, d.Tax));
        if (bill) result.Add(new PostingLine(null, controlAccountId, 0, d.Total, null, party));
        if (bill && d.Deduction > 0) result.Add(PostingLine.CreditKey(PostingKeys.SupplierDeductions, d.Deduction));
        // The deduction is in the text, so the supplier's account statement shows it on the bill's line (revision 6).
        var text = (bill ? (En ? "Supplier bill " : "فاتورة مورد ") : (En ? "Receivable document " : "مستند مدين ")) + d.Number + " · " + partyName
            + (d.Reference is null ? string.Empty : " · " + d.Reference)
            + (d.Deduction > 0 ? (En ? " · deduction " : " · خصم ") + Money.Format(d.Deduction) : string.Empty);
        return new PostingRequest(d.DocumentDate, text.Length > PostingEngine.DescriptionMax ? text[..PostingEngine.DescriptionMax] : text, AccountingCodes.ModuleAcc,
            d.DocumentKind, d.Id, false, result);
    }

    private static string Note(string text) => text.Length <= PostingEngine.NoteMax ? text : text[..PostingEngine.NoteMax];

    /// <summary>Void (FR-127, acct.apar): reason; refused with active payments or returns, in a closed period, or once accepted by e-invoicing.</summary>
    public async Task<ApArDocumentDetails> VoidAsync(string kind, string token, CashVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var k = ApArRules.Kind(kind);
        var why = MasterIds.Reason(r.Reason);
        var doc = await FindAsync(k, token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, doc.Id, ct).ConfigureAwait(false);
        CashSupport.EnsureActive(doc, ErrorCodes.ApArDocumentIsVoid);
        CashSupport.RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(doc, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(doc.DocumentDate, c).ConfigureAwait(false);
            // Payments and returns take the payment/return number first and then change the document under its row version: a void racing
            // them ends in 409, and active ones refuse the void.
            if (await db.Set<ApArPayment>().AnyAsync(p => p.DocumentId == doc.Id && p.Status == CashCodes.Active, c).ConfigureAwait(false)
                || await db.Set<ApArReturn>().AnyAsync(x => x.DocumentId == doc.Id && x.Status == ApArCodes.Posted, c).ConfigureAwait(false))
                throw Fail.Rule(ErrorCodes.ApArDocumentLinked, doc.Number);
            if (k == ApArCodes.Receivable && !await einvoice.CancelApArAsync(doc.Id, null, why, c).ConfigureAwait(false))
                throw Fail.Rule(ErrorCodes.ApArEInvoiced, doc.Number);
            doc.Status = ApArCodes.Void;
            doc.VoidReason = why;
            doc.VoidedBy = cash.UserId;
            doc.VoidedAt = DateTime.UtcNow;
            changes.SetAction(doc, "Void", why);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await ApArPosting.ReverseAsync(db, posting, doc, doc.DocumentKind, (En ? "Void of " : "إلغاء ") + doc.Number, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(doc.Id, ct).ConfigureAwait(false);
    }

    public async Task<ApArDocument> FindAsync(string kind, string token, CancellationToken ct)
    {
        var doc = await support.FindAsync<ApArDocument>(token, ct).ConfigureAwait(false);
        return doc.DocumentKind == kind ? doc : throw Fail.NotFound();
    }

    // ============================================================ reads

    internal sealed record Row(ApArDocument D, Guid PartyPid, string PartyName, Guid? EntryPid, string? EntryNumber, bool Pending, string? EStatus);

    private IQueryable<Row> Rows(IQueryable<ApArDocument> q) => q.Select(d => new Row(d, d.Party.PublicId, d.Party.Name,
        d.JournalEntry != null ? d.JournalEntry.PublicId : null, d.JournalEntry != null ? d.JournalEntry.Number : null,
        db.Set<PendingPosting>().Any(p => p.SourceModule == AccountingCodes.ModuleAcc && p.SourceType == d.DocumentKind && p.SourceId == d.Id && p.ResolvedAt == null),
        db.Set<ApArEInvoiceSubmission>().Where(s => s.ApArDocumentId == d.Id).OrderByDescending(s => s.Id).Select(s => s.Status).FirstOrDefault()));

    private static readonly IReadOnlyDictionary<string, Expression<Func<ApArDocument, object?>>> SortFields = new Dictionary<string, Expression<Func<ApArDocument, object?>>>(StringComparer.Ordinal)
    {
        ["documentDate"] = d => d.DocumentDate,
        ["dueDate"] = d => d.DueDate,
        ["number"] = d => d.Number,
        ["party"] = d => d.Party.Name,
        ["total"] = d => d.Total,
        ["remaining"] = d => d.RemainingAmount,
    };

    private async Task<IQueryable<ApArDocument>> FilterAsync(string kind, ApArDocumentListQuery q, CancellationToken ct)
    {
        var query = db.Set<ApArDocument>().AsNoTracking().Where(d => d.DocumentKind == kind);
        query = (q.Status?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "active" or "posted" => query.Where(d => d.Status == ApArCodes.Posted),
            "void" => query.Where(d => d.Status == ApArCodes.Void),
            "all" => query,
            _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, new FieldRef("status")),
        };
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        query = (q.Payment?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "open" => query.Where(d => d.RemainingAmount > 0),
            "unpaid" => query.Where(d => d.RemainingAmount > 0 && d.RemainingAmount == d.Total),
            "partial" => query.Where(d => d.RemainingAmount > 0 && d.RemainingAmount < d.Total),
            "closed" => query.Where(d => d.RemainingAmount == 0),
            "overdue" => query.Where(d => d.RemainingAmount > 0 && d.DueDate < today),
            // Revision 6 part B (accounting dashboard reminder): open and due within the next 7 days.
            "due-soon" => query.Where(d => d.RemainingAmount > 0 && d.DueDate >= today && d.DueDate <= today.AddDays(Accounting.AccountingDashboardService.DueSoonDays)),
            _ => throw Fail.Field("payment", ErrorCodes.ValidationFormat, new FieldRef("payment")),
        };
        if (!string.IsNullOrWhiteSpace(q.Party))
        {
            var g = support.Decode<ApArParty>(q.Party) ?? throw Fail.Field("party", ErrorCodes.ValidationFormat, new FieldRef("party"));
            query = query.Where(d => d.Party.PublicId == g);
        }
        if (q.From is DateOnly from) query = query.Where(d => d.DocumentDate >= from);
        if (q.To is DateOnly to) query = query.Where(d => d.DocumentDate <= to);
        if (MasterDataSupport.SearchText(q.Q) is { } text) query = query.Where(d => d.Number.Contains(text) || (d.Reference != null && d.Reference.Contains(text)));
        return query;
    }

    public async Task<ApArDocumentPage> ListAsync(string kind, ApArDocumentListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var k = ApArRules.Kind(kind);
        var query = await FilterAsync(k, q, ct).ConfigureAwait(false);
        var paging = new PageRequest(q.Page, q.PageSize);
        var totals = await query.GroupBy(_ => 1).Select(g => new ApArDocumentTotals(g.Count(), g.Sum(d => d.Status == ApArCodes.Posted ? d.Total : 0),
            g.Sum(d => d.Status == ApArCodes.Posted ? d.RemainingAmount : 0), g.Sum(d => d.Status == ApArCodes.Posted ? d.Deduction : 0)))
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? new ApArDocumentTotals(0, 0, 0);
        var rows = await Rows(MasterDataSupport.Sort(query, q.Sort, SortFields, "-documentDate").ThenByDescending(d => d.Id).Skip(paging.Skip).Take(paging.SafePageSize))
            .ToListAsync(ct).ConfigureAwait(false);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        return new ApArDocumentPage(new PagedResult<ApArDocumentResponse>(rows.Select(x => ToResponse(x, today)).ToList(), paging.SafePage, paging.SafePageSize, totals.Count), totals);
    }

    public async Task<GeneratedFile> ExportAsync(string kind, ApArDocumentListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var k = ApArRules.Kind(kind);
        var rows = await Rows(MasterDataSupport.Sort(await FilterAsync(k, q, ct).ConfigureAwait(false), q.Sort, SortFields, "-documentDate").ThenByDescending(d => d.Id)
            .Take(SheetData.MaxRows)).ToListAsync(ct).ConfigureAwait(false);
        string[] headers = En
            ? ["Number", "Date", k == ApArCodes.SupplierBill ? "Supplier" : "Customer", "Reference", "Subtotal", "Tax", "Deduction", "Total", "Paid", "Returned", "Remaining", "Due date", "Status"]
            : ["الرقم", "التاريخ", k == ApArCodes.SupplierBill ? "المورد" : "العميل", "المرجع", "المجموع", "الضريبة", "الخصم", "الإجمالي", "المدفوع", "المرتجع", "المتبقي", "الاستحقاق", "الحالة"];
        var data = rows.Select(x => (IReadOnlyList<object?>)[x.D.Number, x.D.DocumentDate, x.PartyName, x.D.Reference, x.D.Subtotal, x.D.Tax, x.D.Deduction, x.D.Total, x.D.PaidAmount,
            x.D.ReturnedAmount, x.D.RemainingAmount, x.D.DueDate, ApArRules.PaymentStatus(x.D.Status, x.D.Total, x.D.RemainingAmount)]).ToList();
        return await CashExport.FileAsync(k == ApArCodes.SupplierBill ? "supplier-bills" : "receivables", format, headers, data, sheets, exporter, ct).ConfigureAwait(false);
    }

    public async Task<ApArDocumentDetails> GetAsync(string kind, string token, CancellationToken ct)
    {
        var doc = await FindAsync(ApArRules.Kind(kind), token, ct).ConfigureAwait(false);
        return await DetailsAsync(doc.Id, ct).ConfigureAwait(false);
    }

    internal async Task<ApArDocumentDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var row = await Rows(db.Set<ApArDocument>().AsNoTracking().Where(d => d.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        var lines = await db.Set<ApArDocumentLine>().AsNoTracking().Where(l => l.DocumentId == id).OrderBy(l => l.Id).Include(l => l.Account).Include(l => l.CostCentre)
            .ToListAsync(ct).ConfigureAwait(false);
        var paymentIds = await db.Set<ApArPayment>().AsNoTracking().Where(p => p.DocumentId == id).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        var returnIds = await db.Set<ApArReturn>().AsNoTracking().Where(x => x.DocumentId == id).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct).ConfigureAwait(false);
        var payments = new List<ApArPaymentResponse>();
        foreach (var p in paymentIds) payments.Add(await PaymentsReader.ResponseAsync(db, support, cash, p, ct).ConfigureAwait(false));
        var returns = new List<ApArReturnResponse>();
        foreach (var x in returnIds) returns.Add(await ReturnsReader.ResponseAsync(db, support, x, ct).ConfigureAwait(false));
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        return new ApArDocumentDetails(ToResponse(row, today),
            lines.Select(l => new ApArLineResponse(support.Token<ApArDocumentLine>(l.PublicId), l.Description, chart.Ref(l.Account), l.Amount, l.TaxRate,
                l.CostCentre is null ? null : new Ref(support.Token<CostCentre>(l.CostCentre.PublicId), l.CostCentre.Code + " " + l.CostCentre.Name), l.Deduction)).ToList(),
            payments, returns, await cash.WordsAsync(row.D.Total, ct).ConfigureAwait(false), await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false));
    }

    private ApArDocumentResponse ToResponse(Row x, DateOnly today) => new(support.Token<ApArDocument>(x.D.PublicId), x.D.DocumentKind, x.D.Number,
        new Ref(support.Token<ApArParty>(x.PartyPid), x.PartyName), x.D.DocumentDate, x.D.DueDate, x.D.Reference, x.D.Subtotal, x.D.Tax, x.D.Total, x.D.PaidAmount,
        x.D.ReturnedAmount, x.D.RemainingAmount, x.D.Status, ApArRules.PaymentStatus(x.D.Status, x.D.Total, x.D.RemainingAmount),
        x.D.Status == ApArCodes.Posted && x.D.RemainingAmount > 0 && x.D.DueDate < today ? today.DayNumber - x.D.DueDate.DayNumber : 0,
        cash.Entry(x.EntryPid, x.EntryNumber), x.Pending, x.EStatus, x.D.VoidReason, DateTime.SpecifyKind(x.D.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(x.D.RowVersion),
        x.D.Deduction);

    /// <summary>
    /// Remembers (once a day) that a bill with a deduction was refused for the unmapped "Supplier deductions" key, so the accounting dashboard
    /// reminds to map it while such bills are being entered (revision 6 part B). Saved on its own: nothing of the refused bill is saved.
    /// </summary>
    private async Task NoteDeductionAttemptAsync(CancellationToken ct)
    {
        var today = (await support.TodayAsync(ct).ConfigureAwait(false)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var setting = await db.Set<CompanySetting>().SingleOrDefaultAsync(s => s.SettingKey == Configuration.SettingKeys.SupplierDeductionAttempted, ct).ConfigureAwait(false);
        if (setting?.SettingValue == today) return;
        if (setting is null) db.Set<CompanySetting>().Add(new CompanySetting { SettingKey = Configuration.SettingKeys.SupplierDeductionAttempted, SettingValue = today });
        else setting.SettingValue = today;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Pending-posting retries of supplier bills.</summary>
public sealed class SupplierBillPostingHandler(IAppDbContext db, IUrlTokenService tokens, ApArDocumentService documents) : ApArPostingHandler<ApArDocument>(db, tokens)
{
    private readonly IAppDbContext _db = db;

    public override string SourceType => ApArCodes.SupplierBill;

    protected override string VoidCode => ErrorCodes.ApArDocumentIsVoid;

    protected override async Task<PostingRequest?> EntryAsync(ApArDocument d, CancellationToken ct) => await ApArDocumentEntry.BuildAsync(_db, documents, d, ct).ConfigureAwait(false);
}

/// <summary>Pending-posting retries of receivable documents.</summary>
public sealed class ReceivablePostingHandler(IAppDbContext db, IUrlTokenService tokens, ApArDocumentService documents) : ApArPostingHandler<ApArDocument>(db, tokens)
{
    private readonly IAppDbContext _db = db;

    public override string SourceType => ApArCodes.Receivable;

    protected override string VoidCode => ErrorCodes.ApArDocumentIsVoid;

    protected override async Task<PostingRequest?> EntryAsync(ApArDocument d, CancellationToken ct) => await ApArDocumentEntry.BuildAsync(_db, documents, d, ct).ConfigureAwait(false);
}

internal static class ApArDocumentEntry
{
    public static async Task<PostingRequest> BuildAsync(IAppDbContext db, ApArDocumentService documents, ApArDocument d, CancellationToken ct)
    {
        var lines = await db.Set<ApArDocumentLine>().AsNoTracking().Where(l => l.DocumentId == d.Id).OrderBy(l => l.Id).ToListAsync(ct).ConfigureAwait(false);
        var party = await db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).Where(p => p.Id == d.PartyId).Select(p => new { p.ControlAccountId, p.Name }).SingleAsync(ct).ConfigureAwait(false);
        return documents.BuildEntry(d, lines, party.ControlAccountId, party.Name);
    }
}
