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
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.ApAr;

/// <summary>
/// Returns on supplier bills and receivable documents (FR-126): an amount before tax; the tax is the document's tax share (a return of
/// everything not yet returned takes exactly the tax not yet returned); the total at most the document's remaining balance — a total
/// above it by rounding (≤ 0.010) takes exactly the remaining balance (as DT-80). Mirror entry for the returned share: the amount split
/// over the document's lines (accounts and cost centres) by their amounts, the tax on sales tax input/output, the total on the party's
/// control account. Receivable returns are e-invoiced as credit notes when their document is (D-15). Void by reversal with a reason.
/// </summary>
public sealed class ApArReturnService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, CashSupport cash, IChangeContext changes, IRecordLockGuard locks,
    IPeriodGuard periods, INumberingService numbering, PostingEngine posting, EInvoiceService einvoice, ISpreadsheetService sheets, Reports.ReportExporter exporter)
{
    public const string EntityType = "ACC.ApArReturn";
    public const string SourceType = "ApArReturn";

    /// <summary>Largest rounding difference absorbed by the remaining balance (as DT-80).</summary>
    public const decimal RoundingTolerance = 0.010m;

    private bool En => ctx.Language == "en";

    public async Task<(ApArReturnDetails Value, bool Created)> CreateAsync(ApArReturnRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<ApArReturn>(clientId, ct).ConfigureAwait(false) is { } again) return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);
        var reason = TextNormalizer.CleanText(r.Reason);
        new Validator().Check(!string.IsNullOrWhiteSpace(r.DocumentId), "documentId", ErrorCodes.ValidationRequired).Require("reason", reason).MaxLength("reason", reason, 500)
            .ThrowIfInvalid();
        var (_, decimals) = await cash.CurrencyAsync(ct).ConfigureAwait(false);
        var amount = CashSupport.Amount(r.Amount, decimals);
        var date = await cash.DateAsync(r.ReturnDate, "returnDate", ct).ConfigureAwait(false);
        var pid = support.Decode<ApArDocument>(r.DocumentId) ?? throw Fail.Field("documentId", ErrorCodes.ValidationFormat, new FieldRef("documentId"));
        var doc = await db.Set<ApArDocument>().SingleOrDefaultAsync(d => d.PublicId == pid, ct).ConfigureAwait(false) ?? throw Fail.Field("documentId", ErrorCodes.ValidationFormat, new FieldRef("documentId"));
        await locks.EnsureNotLockedByOthersAsync(ApArDocumentService.EntityType, doc.Id, ct).ConfigureAwait(false);
        if (date < doc.DocumentDate) throw Fail.Field("returnDate", ErrorCodes.ValidationRange, new FieldRef("returnDate"));

        var ret = new ApArReturn { DocumentId = doc.Id, ReturnDate = date, Amount = amount, Reason = reason!, Status = ApArCodes.Posted };
        if (clientId is Guid id) ret.PublicId = id;
        ApArReturn? raced = null;
        try
        {
            await db.InTransactionAsync(async c =>
            {
                await posting.EnsurePostingReadyAsync(c).ConfigureAwait(false);
                await periods.EnsureOpenAsync(date, c).ConfigureAwait(false);
                ret.Number = await numbering.NextAsync(Configuration.DocumentTypes.ApArReturn, c).ConfigureAwait(false);
                await db.Set<ApArDocument>().Entry(doc).ReloadAsync(c).ConfigureAwait(false);
                if (doc.Status != ApArCodes.Posted) throw Fail.RuleOn("documentId", ErrorCodes.ApArDocumentIsVoid, doc.Number);
                if (doc.RemainingAmount <= 0) throw Fail.RuleOn("documentId", ErrorCodes.ApArDocumentNotOpen, doc.Number);
                var earlier = await db.Set<ApArReturn>().Where(x => x.DocumentId == doc.Id && x.Status == ApArCodes.Posted)
                    .GroupBy(_ => 1).Select(g => new { Amount = g.Sum(x => x.Amount), Tax = g.Sum(x => x.Tax), Deduction = g.Sum(x => x.Deduction) })
                    .SingleOrDefaultAsync(c).ConfigureAwait(false);
                var returnedAmount = earlier?.Amount ?? 0;
                var returnedTax = earlier?.Tax ?? 0;
                var returnedDeduction = earlier?.Deduction ?? 0;
                if (amount > doc.Subtotal - returnedAmount)
                    throw Fail.RuleOn("amount", ErrorCodes.ApArExceedsRemaining, Money.Format(amount), Money.Format(doc.Subtotal - returnedAmount));
                var last = amount == doc.Subtotal - returnedAmount;
                ret.Tax = last ? doc.Tax - returnedTax : doc.Subtotal == 0 ? 0 : Money.Round(doc.Tax * amount / doc.Subtotal, decimals);
                // Owner decision 17 Oct 2026: a return gives back the bill's deduction in proportion (the last return the rest), so a
                // full return cancels the whole bill; the limit is the remaining payable plus the returned share of the deduction.
                ret.Deduction = last ? doc.Deduction - returnedDeduction : doc.Subtotal == 0 ? 0 : Money.Round(doc.Deduction * amount / doc.Subtotal, decimals);
                ret.Total = amount + ret.Tax - ret.Deduction;
                if (ret.Total > doc.RemainingAmount)
                {
                    var over = ret.Total - doc.RemainingAmount;
                    if (over > RoundingTolerance || ret.Tax < over)
                        throw Fail.RuleOn("amount", ErrorCodes.ApArExceedsRemaining, Money.Format(ret.Total), Money.Format(doc.RemainingAmount));
                    ret.Tax -= over;
                    ret.Total = doc.RemainingAmount;
                }
                doc.ReturnedAmount += ret.Total;
                ApArRules.Rebalance(doc);
                changes.SetAction(doc, "Return", ret.Number);
                db.Set<ApArReturn>().Add(ret);
                if (await support.SaveCreateAsync<ApArReturn>(clientId, c).ConfigureAwait(false) is { } other) throw new RacedCreate(other);
                var entry = await posting.PostOrPendAsync(await ApArReturnEntry.BuildAsync(db, this, ret, doc, c).ConfigureAwait(false), c).ConfigureAwait(false);
                ret.JournalEntryId = entry?.Id;
                await db.SaveChangesAsync(c).ConfigureAwait(false);
                if (doc.DocumentKind == ApArCodes.Receivable) await einvoice.QueueApArReturnAsync(ret.Id, doc.Id, c).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (RacedCreate rc)
        {
            db.ChangeTracker.Clear();
            raced = rc.Existing;
        }
        return raced is not null ? (await DetailsAsync(raced.Id, ct).ConfigureAwait(false), false) : (await DetailsAsync(ret.Id, ct).ConfigureAwait(false), true);
    }

    private sealed class RacedCreate(ApArReturn existing) : Exception
    {
        public ApArReturn Existing { get; } = existing;
    }

    /// <summary>
    /// Mirror entry of the returned share: bill return — Dr the supplier's control account (total, net of the deduction share) and "Supplier
    /// deductions" (the returned deduction share), Cr the line accounts (amount split by line amounts) and sales tax input; receivable return —
    /// Dr the line accounts and sales tax output, Cr the customer's control account.
    /// </summary>
    public PostingRequest BuildEntry(ApArReturn ret, ApArDocument doc, IReadOnlyList<ApArDocumentLine> lines, long controlAccountId, string partyName, int decimals)
    {
        ArgumentNullException.ThrowIfNull(ret);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(lines);
        var party = new PostingParty(AccountingCodes.ModuleAcc, AccountingCodes.PartyApAr, doc.PartyId);
        var bill = doc.DocumentKind == ApArCodes.SupplierBill;
        var shares = EInvoiceDocumentBuilder.Split(ret.Amount, lines.Select(l => l.Amount).ToList(), decimals);
        var result = new List<PostingLine>();
        if (bill) result.Add(new PostingLine(null, controlAccountId, ret.Total, 0, null, party));
        if (bill && ret.Deduction > 0) result.Add(PostingLine.DebitKey(PostingKeys.SupplierDeductions, ret.Deduction));
        for (var i = 0; i < lines.Count; i++)
        {
            if (shares[i] == 0) continue;
            result.Add(bill ? new PostingLine(null, lines[i].AccountId, 0, shares[i], lines[i].CostCentreId) : new PostingLine(null, lines[i].AccountId, shares[i], 0, lines[i].CostCentreId));
        }
        if (ret.Tax > 0) result.Add(bill ? PostingLine.CreditKey(PostingKeys.SalesTaxInput, ret.Tax) : PostingLine.DebitKey(PostingKeys.SalesTaxOutput, ret.Tax));
        if (!bill) result.Add(new PostingLine(null, controlAccountId, 0, ret.Total, null, party));
        var text = (En ? "Return " : "مرتجع ") + ret.Number + " · " + doc.Number + " · " + partyName;
        return new PostingRequest(ret.ReturnDate, text.Length > PostingEngine.DescriptionMax ? text[..PostingEngine.DescriptionMax] : text, AccountingCodes.ModuleAcc,
            SourceType, ret.Id, false, result);
    }

    /// <summary>Void (FR-127): reason; refused once accepted by e-invoicing; reversal dated like the return; the document's balance restored.</summary>
    public async Task<ApArReturnDetails> VoidAsync(string token, CashVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var ret = await support.FindAsync<ApArReturn>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, ret.Id, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(ApArDocumentService.EntityType, ret.DocumentId, ct).ConfigureAwait(false);
        CashSupport.EnsureActive(ret, ErrorCodes.ApArReturnIsVoid);
        CashSupport.RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(ret, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(ret.ReturnDate, c).ConfigureAwait(false);
            if (!await einvoice.CancelApArAsync(null, ret.Id, why, c).ConfigureAwait(false)) throw Fail.Rule(ErrorCodes.ApArReturnEInvoiced, ret.Number);
            ret.Status = ApArCodes.Void;
            ret.VoidReason = why;
            ret.VoidedBy = cash.UserId;
            ret.VoidedAt = DateTime.UtcNow;
            changes.SetAction(ret, "Void", why);
            var doc = await db.Set<ApArDocument>().SingleAsync(d => d.Id == ret.DocumentId, c).ConfigureAwait(false);
            doc.ReturnedAmount -= ret.Total;
            ApArRules.Rebalance(doc);
            changes.SetAction(doc, "ReturnVoid", ret.Number);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await ApArPosting.ReverseAsync(db, posting, ret, SourceType, (En ? "Void of " : "إلغاء ") + ret.Number, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(ret.Id, ct).ConfigureAwait(false);
    }

    // ============================================================ reads

    private static readonly IReadOnlyDictionary<string, Expression<Func<ApArReturn, object?>>> SortFields = new Dictionary<string, Expression<Func<ApArReturn, object?>>>(StringComparer.Ordinal)
    {
        ["returnDate"] = x => x.ReturnDate,
        ["number"] = x => x.Number,
        ["party"] = x => x.Document.Party.Name,
        ["total"] = x => x.Total,
    };

    private IQueryable<ApArReturn> Filter(string kind, ApArReturnListQuery q)
    {
        var query = db.Set<ApArReturn>().AsNoTracking().Where(x => x.Document.DocumentKind == kind);
        query = (q.Status?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "active" or "posted" => query.Where(x => x.Status == ApArCodes.Posted),
            "void" => query.Where(x => x.Status == ApArCodes.Void),
            "all" => query,
            _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, new FieldRef("status")),
        };
        if (!string.IsNullOrWhiteSpace(q.Party))
        {
            var g = support.Decode<ApArParty>(q.Party) ?? throw Fail.Field("party", ErrorCodes.ValidationFormat, new FieldRef("party"));
            query = query.Where(x => x.Document.Party.PublicId == g);
        }
        if (!string.IsNullOrWhiteSpace(q.Document))
        {
            var g = support.Decode<ApArDocument>(q.Document) ?? throw Fail.Field("document", ErrorCodes.ValidationFormat, new FieldRef("document"));
            query = query.Where(x => x.Document.PublicId == g);
        }
        if (q.From is DateOnly from) query = query.Where(x => x.ReturnDate >= from);
        if (q.To is DateOnly to) query = query.Where(x => x.ReturnDate <= to);
        if (MasterDataSupport.SearchText(q.Q) is { } text) query = query.Where(x => x.Number.Contains(text) || x.Document.Number.Contains(text));
        return query;
    }

    public async Task<ApArReturnPage> ListAsync(string kind, ApArReturnListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = Filter(ApArRules.Kind(kind), q);
        var paging = new PageRequest(q.Page, q.PageSize);
        var totals = await query.GroupBy(_ => 1).Select(g => new CashListTotals(g.Count(), g.Sum(x => x.Status == ApArCodes.Posted ? x.Total : 0), g.Count(x => x.Status == ApArCodes.Posted))).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? new CashListTotals(0, 0);
        var ids = await MasterDataSupport.Sort(query, q.Sort, SortFields, "-returnDate").ThenByDescending(x => x.Id).Skip(paging.Skip).Take(paging.SafePageSize)
            .Select(x => x.Id).ToListAsync(ct).ConfigureAwait(false);
        var items = new List<ApArReturnResponse>();
        foreach (var id in ids) items.Add(await ReturnsReader.ResponseAsync(db, support, id, ct).ConfigureAwait(false));
        return new ApArReturnPage(new PagedResult<ApArReturnResponse>(items, paging.SafePage, paging.SafePageSize, totals.Count), totals);
    }

    public async Task<GeneratedFile> ExportAsync(string kind, ApArReturnListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var k = ApArRules.Kind(kind);
        var rows = await MasterDataSupport.Sort(Filter(k, q), q.Sort, SortFields, "-returnDate").ThenByDescending(x => x.Id).Take(SheetData.MaxRows)
            .Select(x => new { x.Number, x.ReturnDate, Party = x.Document.Party.Name, Doc = x.Document.Number, x.Amount, x.Tax, x.Deduction, x.Total, x.Reason, x.Status })
            .ToListAsync(ct).ConfigureAwait(false);
        string[] headers = En
            ? ["Number", "Date", k == ApArCodes.SupplierBill ? "Supplier" : "Customer", "Document", "Amount", "Tax", "Deduction", "Total", "Reason", "Status"]
            : ["الرقم", "التاريخ", k == ApArCodes.SupplierBill ? "المورد" : "العميل", "المستند", "المبلغ", "الضريبة", "الخصم", "الإجمالي", "السبب", "الحالة"];
        var data = rows.Select(x => (IReadOnlyList<object?>)[x.Number, x.ReturnDate, x.Party, x.Doc, x.Amount, x.Tax, x.Deduction, x.Total, x.Reason, x.Status]).ToList();
        return await CashExport.FileAsync(k == ApArCodes.SupplierBill ? "payable-returns" : "receivable-returns", format, headers, data, sheets, exporter, ct).ConfigureAwait(false);
    }

    public async Task<ApArReturnDetails> GetAsync(string token, CancellationToken ct)
    {
        var x = await support.FindAsync<ApArReturn>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(x.Id, ct).ConfigureAwait(false);
    }

    internal async Task<ApArReturnDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var response = await ReturnsReader.ResponseAsync(db, support, id, ct).ConfigureAwait(false);
        return new ApArReturnDetails(response, await cash.WordsAsync(response.Total, ct).ConfigureAwait(false), await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false));
    }
}

/// <summary>Reads a return as shown on lists, documents and print-outs.</summary>
internal static class ReturnsReader
{
    public static async Task<ApArReturnResponse> ResponseAsync(IAppDbContext db, MasterDataSupport support, long id, CancellationToken ct)
    {
        var x = await db.Set<ApArReturn>().AsNoTracking().Where(r => r.Id == id).Select(r => new
        {
            R = r, r.Document.DocumentKind, DocPid = r.Document.PublicId, DocNumber = r.Document.Number, PartyPid = r.Document.Party.PublicId, PartyName = r.Document.Party.Name,
            EntryPid = r.JournalEntry != null ? (Guid?)r.JournalEntry.PublicId : null, EntryNumber = r.JournalEntry != null ? r.JournalEntry.Number : null,
            Pending = db.Set<PendingPosting>().Any(q => q.SourceModule == AccountingCodes.ModuleAcc && q.SourceType == ApArReturnService.SourceType && q.SourceId == r.Id && q.ResolvedAt == null),
            EStatus = db.Set<ApArEInvoiceSubmission>().Where(s => s.ApArReturnId == r.Id).OrderByDescending(s => s.Id).Select(s => s.Status).FirstOrDefault(),
        }).SingleAsync(ct).ConfigureAwait(false);
        return new ApArReturnResponse(support.Token<ApArReturn>(x.R.PublicId), x.R.Number, x.DocumentKind, new DocRef(support.Token<ApArDocument>(x.DocPid), x.DocNumber),
            new Ref(support.Token<ApArParty>(x.PartyPid), x.PartyName), x.R.ReturnDate, x.R.Amount, x.R.Tax, x.R.Total, x.R.Reason, x.R.Status,
            x.EntryPid is Guid g ? new DocRef(support.Token<JournalEntry>(g), x.EntryNumber) : null, x.Pending, x.EStatus, x.R.VoidReason,
            DateTime.SpecifyKind(x.R.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(x.R.RowVersion), x.R.Deduction);
    }
}

internal static class ApArReturnEntry
{
    public static async Task<PostingRequest> BuildAsync(IAppDbContext db, ApArReturnService returns, ApArReturn ret, ApArDocument doc, CancellationToken ct)
    {
        var lines = await db.Set<ApArDocumentLine>().AsNoTracking().Where(l => l.DocumentId == doc.Id).OrderBy(l => l.Id).ToListAsync(ct).ConfigureAwait(false);
        var party = await db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).Where(p => p.Id == doc.PartyId).Select(p => new { p.ControlAccountId, p.Name }).SingleAsync(ct).ConfigureAwait(false);
        var decimals = await db.Set<Domain.Core.Company>().AsNoTracking().Where(c => c.Id == doc.CompanyId).Select(c => (int)c.CurrencyDecimals).SingleAsync(ct).ConfigureAwait(false);
        return returns.BuildEntry(ret, doc, lines, party.ControlAccountId, party.Name, decimals);
    }
}

/// <summary>Pending-posting retries of AP/AR returns.</summary>
public sealed class ApArReturnPostingHandler(IAppDbContext db, IUrlTokenService tokens, ApArReturnService returns) : ApArPostingHandler<ApArReturn>(db, tokens)
{
    private readonly IAppDbContext _db = db;

    public override string SourceType => ApArReturnService.SourceType;

    protected override string VoidCode => ErrorCodes.ApArReturnIsVoid;

    protected override async Task<PostingRequest?> EntryAsync(ApArReturn r, CancellationToken ct)
    {
        var doc = await _db.Set<ApArDocument>().AsNoTracking().SingleAsync(d => d.Id == r.DocumentId, ct).ConfigureAwait(false);
        return await ApArReturnEntry.BuildAsync(_db, returns, r, doc, ct).ConfigureAwait(false);
    }
}
