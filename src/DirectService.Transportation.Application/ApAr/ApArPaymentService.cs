using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Cash;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Accounting.Contracts.Posting;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.ApAr;

/// <summary>
/// Payments to suppliers and collections from customers (FR-125): against one posted document with a remaining balance, full or partial
/// (≤ remaining, checked again under the PAY- number lock and the document's row version), by cash (cash box), bank transfer or CliQ
/// (bank account, reference required); posts — payment: Dr the supplier's control account with the party, Cr cash/bank; collection:
/// Dr cash/bank, Cr the customer's control account — and updates the document's paid and remaining amounts. Void by reversal with a
/// reason restores the balance (FR-127).
/// </summary>
public sealed class ApArPaymentService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, CashSupport cash, IChangeContext changes, IRecordLockGuard locks,
    IPeriodGuard periods, INumberingService numbering, IAccountingPosting posting, ISpreadsheetService sheets, IReportExporter exporter)
{
    public const string EntityType = "ACC.ApArPayment";
    public const string SourceType = "ApArPayment";

    private bool En => ctx.Language == "en";

    public async Task<(ApArPaymentDetails Value, bool Created)> CreateAsync(ApArPaymentRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<ApArPayment>(clientId, ct).ConfigureAwait(false) is { } again) return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);
        new Validator().Check(!string.IsNullOrWhiteSpace(r.DocumentId), "documentId", ErrorCodes.ValidationRequired)
            .Check(r.AttachmentsCount is null or (>= 0 and <= 999), "attachmentsCount", ErrorCodes.ValidationRange).ThrowIfInvalid();
        var (_, decimals) = await cash.CurrencyAsync(ct).ConfigureAwait(false);
        var amount = CashSupport.Amount(r.Amount, decimals);
        var date = await cash.DateAsync(r.PaymentDate, "paymentDate", ct).ConfigureAwait(false);
        var pid = support.Decode<ApArDocument>(r.DocumentId) ?? throw Fail.Field("documentId", ErrorCodes.ValidationFormat, new FieldRef("documentId"));
        var doc = await db.Set<ApArDocument>().SingleOrDefaultAsync(d => d.PublicId == pid, ct).ConfigureAwait(false) ?? throw Fail.Field("documentId", ErrorCodes.ValidationFormat, new FieldRef("documentId"));
        await locks.EnsureNotLockedByOthersAsync(ApArDocumentService.EntityType, doc.Id, ct).ConfigureAwait(false);
        if (date < doc.DocumentDate) throw Fail.Field("paymentDate", ErrorCodes.ValidationRange, new FieldRef("paymentDate"));
        var money = await cash.ChooseAsync(r.PaymentMethod, r.CashBoxId, r.BankAccountId, r.Reference, ct).ConfigureAwait(false);

        var payment = new ApArPayment
        {
            DocumentId = doc.Id, PaymentDate = date, Amount = amount, PaymentMethod = money.Method, CashBoxId = money.CashBoxId, BankAccountId = money.BankAccountId,
            Reference = money.Reference, AttachmentsCount = r.AttachmentsCount ?? 0, Status = CashCodes.Active,
        };
        if (clientId is Guid id) payment.PublicId = id;
        ApArPayment? raced = null;
        try
        {
            await db.InTransactionAsync(async c =>
            {
                await posting.EnsureReadyAsync(AccountingCodes.ModuleAcc, c).ConfigureAwait(false);
                await periods.EnsureOpenAsync(date, c).ConfigureAwait(false);
                // The payment number serialises payments of the company; the document is read again under it and saved with its row
                // version, so a return racing the payment ends in 409 (DT-95).
                payment.Number = await numbering.NextAsync(Configuration.DocumentTypes.ApArPayment, c).ConfigureAwait(false);
                await db.Set<ApArDocument>().Entry(doc).ReloadAsync(c).ConfigureAwait(false);
                if (doc.Status != ApArCodes.Posted) throw Fail.RuleOn("documentId", ErrorCodes.ApArDocumentIsVoid, doc.Number);
                if (doc.RemainingAmount <= 0) throw Fail.RuleOn("documentId", ErrorCodes.ApArDocumentNotOpen, doc.Number);
                if (amount > doc.RemainingAmount) throw Fail.RuleOn("amount", ErrorCodes.ApArExceedsRemaining, Money.Format(amount), Money.Format(doc.RemainingAmount));
                doc.PaidAmount += amount;
                ApArRules.Rebalance(doc);
                changes.SetAction(doc, "Payment", payment.Number);
                db.Set<ApArPayment>().Add(payment);
                if (await support.SaveCreateAsync<ApArPayment>(clientId, c).ConfigureAwait(false) is { } other) throw new RacedCreate(other);
                var party = await db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).Where(p => p.Id == doc.PartyId).Select(p => new { p.ControlAccountId, p.Name })
                    .SingleAsync(c).ConfigureAwait(false);
                var entry = await posting.PostOrPendAsync(BuildEntry(payment, doc, party.ControlAccountId, party.Name, money.AccountId), c).ConfigureAwait(false);
                payment.JournalEntryId = entry?.JournalEntryId;
                await db.SaveChangesAsync(c).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (RacedCreate rc)
        {
            db.ChangeTracker.Clear();
            raced = rc.Existing;
        }
        return raced is not null ? (await DetailsAsync(raced.Id, ct).ConfigureAwait(false), false) : (await DetailsAsync(payment.Id, ct).ConfigureAwait(false), true);
    }

    private sealed class RacedCreate(ApArPayment existing) : Exception
    {
        public ApArPayment Existing { get; } = existing;
    }

    /// <summary>Payment: Dr supplier control (party), Cr cash/bank. Collection: Dr cash/bank, Cr customer control (party).</summary>
    public PostingDocument BuildEntry(ApArPayment p, ApArDocument doc, long controlAccountId, string partyName, long moneyAccountId)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(doc);
        var party = new PartyRef(AccountingCodes.ModuleAcc, AccountingCodes.PartyApAr, doc.PartyId);
        var bill = doc.DocumentKind == ApArCodes.SupplierBill;
        var text = (bill ? (En ? "Payment " : "دفعة ") : (En ? "Collection " : "تحصيل ")) + p.Number + " · " + doc.Number + " · " + partyName;
        IReadOnlyList<PostingLineSpec> lines = bill
            ? [new PostingLineSpec(null, controlAccountId, p.Amount, 0, null, party), CashSupport.Credit(moneyAccountId, p.Amount)]
            : [CashSupport.Debit(moneyAccountId, p.Amount), new PostingLineSpec(null, controlAccountId, 0, p.Amount, null, party)];
        return PostingEntry.Of(p.PaymentDate, text.Length > PostingRules.DescriptionMax ? text[..PostingRules.DescriptionMax] : text, AccountingCodes.ModuleAcc,
            SourceType, p.Id, lines);
    }

    /// <summary>Void (FR-127): reason, reversal dated like the payment, the document's balance restored, number kept.</summary>
    public async Task<ApArPaymentDetails> VoidAsync(string token, CashVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var payment = await support.FindAsync<ApArPayment>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, payment.Id, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(ApArDocumentService.EntityType, payment.DocumentId, ct).ConfigureAwait(false);
        CashSupport.EnsureActive(payment, ErrorCodes.ApArPaymentIsVoid);
        CashSupport.RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(payment, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(payment.PaymentDate, c).ConfigureAwait(false);
            payment.Status = CashCodes.Void;
            payment.VoidReason = why;
            payment.VoidedBy = cash.UserId;
            payment.VoidedAt = DateTime.UtcNow;
            changes.SetAction(payment, "Void", why);
            var doc = await db.Set<ApArDocument>().SingleAsync(d => d.Id == payment.DocumentId, c).ConfigureAwait(false);
            doc.PaidAmount -= payment.Amount;
            ApArRules.Rebalance(doc);
            changes.SetAction(doc, "PaymentVoid", payment.Number);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await ApArPosting.ReverseAsync(db, posting, payment, SourceType, (En ? "Void of " : "إلغاء ") + payment.Number, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(payment.Id, ct).ConfigureAwait(false);
    }

    // ============================================================ reads

    private static readonly IReadOnlyDictionary<string, Expression<Func<ApArPayment, object?>>> SortFields = new Dictionary<string, Expression<Func<ApArPayment, object?>>>(StringComparer.Ordinal)
    {
        ["paymentDate"] = p => p.PaymentDate,
        ["number"] = p => p.Number,
        ["party"] = p => p.Document.Party.Name,
        ["amount"] = p => p.Amount,
    };

    private IQueryable<ApArPayment> Filter(string kind, ApArPaymentListQuery q)
    {
        var query = CashSupport.WithStatus(db.Set<ApArPayment>().AsNoTracking().Where(p => p.Document.DocumentKind == kind), q.Status);
        if (!string.IsNullOrWhiteSpace(q.Party))
        {
            var g = support.Decode<ApArParty>(q.Party) ?? throw Fail.Field("party", ErrorCodes.ValidationFormat, new FieldRef("party"));
            query = query.Where(p => p.Document.Party.PublicId == g);
        }
        if (!string.IsNullOrWhiteSpace(q.Document))
        {
            var g = support.Decode<ApArDocument>(q.Document) ?? throw Fail.Field("document", ErrorCodes.ValidationFormat, new FieldRef("document"));
            query = query.Where(p => p.Document.PublicId == g);
        }
        if (q.From is DateOnly from) query = query.Where(p => p.PaymentDate >= from);
        if (q.To is DateOnly to) query = query.Where(p => p.PaymentDate <= to);
        if (CashSupport.Method(q.Method) is { } m) query = query.Where(p => p.PaymentMethod == m);
        if (MasterDataSupport.SearchText(q.Q) is { } text)
            query = query.Where(p => p.Number.Contains(text) || (p.Reference != null && p.Reference.Contains(text)) || p.Document.Number.Contains(text));
        return query;
    }

    public async Task<ApArPaymentPage> ListAsync(string kind, ApArPaymentListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = Filter(ApArRules.Kind(kind), q);
        var paging = new PageRequest(q.Page, q.PageSize);
        var totals = await query.GroupBy(_ => 1).Select(g => new CashListTotals(g.Count(), g.Sum(p => p.Status == CashCodes.Active ? p.Amount : 0), g.Count(p => p.Status == CashCodes.Active))).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? new CashListTotals(0, 0);
        var ids = await MasterDataSupport.Sort(query, q.Sort, SortFields, "-paymentDate").ThenByDescending(p => p.Id).Skip(paging.Skip).Take(paging.SafePageSize)
            .Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        var items = new List<ApArPaymentResponse>();
        foreach (var id in ids) items.Add(await PaymentsReader.ResponseAsync(db, support, cash, id, ct).ConfigureAwait(false));
        return new ApArPaymentPage(new PagedResult<ApArPaymentResponse>(items, paging.SafePage, paging.SafePageSize, totals.Count), totals);
    }

    public async Task<GeneratedFile> ExportAsync(string kind, ApArPaymentListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var k = ApArRules.Kind(kind);
        var rows = await MasterDataSupport.Sort(Filter(k, q), q.Sort, SortFields, "-paymentDate").ThenByDescending(p => p.Id).Take(SheetData.MaxRows)
            .Select(p => new { p.Number, p.PaymentDate, Party = p.Document.Party.Name, Doc = p.Document.Number, p.Amount, p.PaymentMethod,
                Account = p.CashBox != null ? p.CashBox.Name : p.BankAccount!.Name, p.Reference, p.Status })
            .ToListAsync(ct).ConfigureAwait(false);
        string[] headers = En
            ? ["Number", "Date", k == ApArCodes.SupplierBill ? "Supplier" : "Customer", "Document", "Amount", "Method", "Cash box / bank", "Reference", "Status"]
            : ["الرقم", "التاريخ", k == ApArCodes.SupplierBill ? "المورد" : "العميل", "المستند", "المبلغ", "طريقة الدفع", "الصندوق / البنك", "المرجع", "الحالة"];
        var data = rows.Select(x => (IReadOnlyList<object?>)[x.Number, x.PaymentDate, x.Party, x.Doc, x.Amount, x.PaymentMethod, x.Account, x.Reference, x.Status]).ToList();
        return await CashExport.FileAsync(k == ApArCodes.SupplierBill ? "supplier-payments" : "collections", format, headers, data, sheets, exporter, ct).ConfigureAwait(false);
    }

    public async Task<ApArPaymentDetails> GetAsync(string token, CancellationToken ct)
    {
        var p = await support.FindAsync<ApArPayment>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(p.Id, ct).ConfigureAwait(false);
    }

    internal async Task<ApArPaymentDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var response = await PaymentsReader.ResponseAsync(db, support, cash, id, ct).ConfigureAwait(false);
        return new ApArPaymentDetails(response, await cash.WordsAsync(response.Amount, ct).ConfigureAwait(false), await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false));
    }
}

/// <summary>Reads a payment as shown on lists, documents and vouchers.</summary>
internal static class PaymentsReader
{
    public static async Task<ApArPaymentResponse> ResponseAsync(IAppDbContext db, MasterDataSupport support, CashSupport cash, long id, CancellationToken ct)
    {
        var x = await db.Set<ApArPayment>().AsNoTracking().Where(p => p.Id == id).Select(p => new
        {
            P = p, p.Document.DocumentKind, DocPid = p.Document.PublicId, DocNumber = p.Document.Number, PartyPid = p.Document.Party.PublicId, PartyName = p.Document.Party.Name,
            BoxPid = p.CashBox != null ? (Guid?)p.CashBox.PublicId : null, BoxName = p.CashBox != null ? p.CashBox.Name : null,
            BankPid = p.BankAccount != null ? (Guid?)p.BankAccount.PublicId : null, BankName = p.BankAccount != null ? p.BankAccount.Name : null,
            EntryPid = p.JournalEntry != null ? (Guid?)p.JournalEntry.PublicId : null, EntryNumber = p.JournalEntry != null ? p.JournalEntry.Number : null,
            Pending = db.Set<PendingPosting>().Any(q => q.SourceModule == AccountingCodes.ModuleAcc && q.SourceType == ApArPaymentService.SourceType && q.SourceId == p.Id && q.ResolvedAt == null),
        }).SingleAsync(ct).ConfigureAwait(false);
        return new ApArPaymentResponse(support.Token<ApArPayment>(x.P.PublicId), x.P.Number, x.DocumentKind, new DocRef(support.Token<ApArDocument>(x.DocPid), x.DocNumber),
            new Ref(support.Token<ApArParty>(x.PartyPid), x.PartyName), x.P.PaymentDate, x.P.Amount, x.P.PaymentMethod, cash.Account(x.BoxPid, x.BoxName, x.BankPid, x.BankName),
            x.P.Reference, x.P.AttachmentsCount, x.P.Status, cash.Entry(x.EntryPid, x.EntryNumber), x.Pending, x.P.VoidReason,
            DateTime.SpecifyKind(x.P.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(x.P.RowVersion));
    }
}

/// <summary>Pending-posting retries of AP/AR payments.</summary>
public sealed class ApArPaymentPostingHandler(IAppDbContext db, IUrlTokenService tokens, ApArPaymentService payments, CashSupport cash) : ApArPostingHandler<ApArPayment>(db, tokens)
{
    private readonly IAppDbContext _db = db;

    public override string SourceType => ApArPaymentService.SourceType;

    protected override string VoidCode => ErrorCodes.ApArPaymentIsVoid;

    protected override async Task<PostingDocument?> EntryAsync(ApArPayment p, CancellationToken ct)
    {
        var doc = await _db.Set<ApArDocument>().AsNoTracking().SingleAsync(d => d.Id == p.DocumentId, ct).ConfigureAwait(false);
        var party = await _db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).Where(x => x.Id == doc.PartyId).Select(x => new { x.ControlAccountId, x.Name }).SingleAsync(ct).ConfigureAwait(false);
        return payments.BuildEntry(p, doc, party.ControlAccountId, party.Name, await cash.AccountIdAsync(p.CashBoxId, p.BankAccountId, ct).ConfigureAwait(false));
    }
}
