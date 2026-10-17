using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Invoicing;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// Customer receipts (FR-067 … FR-071, 9.9): applied to one or several of the customer's open invoices (revision 6 — each allocation ≤ that
/// invoice's remaining balance, which it reduces and whose status it updates; together ≤ the receipt amount, the rest on account) or on account; numbered from the receipt sequence; entry Dr cash/bank, Cr trade receivables with the
/// customer; void with reason (reversal, invoice balance restored, number kept); list with filters, totals and export.
/// </summary>
public sealed class ReceiptService(IAppDbContext db, MasterDataSupport support, CashSupport cash, IChangeContext changes, IRecordLockGuard locks, IPeriodGuard periods,
    INumberingService numbering, PostingEngine posting, ISpreadsheetService sheets, Reports.ReportExporter exporter)
{
    public const string EntityType = "TRN.Receipt";
    public const string SourceType = "Receipt";

    public async Task<(ReceiptDetails Value, bool Created)> CreateAsync(ReceiptRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Receipt>(clientId, ct).ConfigureAwait(false) is { } again) return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);
        var notes = TextNormalizer.CleanText(r.Notes);
        new Validator().Check(!string.IsNullOrWhiteSpace(r.CustomerId), "customerId", ErrorCodes.ValidationRequired).MaxLength("notes", notes, 500)
            .Check(r.AttachmentsCount is null or (>= 0 and <= 999), "attachmentsCount", ErrorCodes.ValidationRange).ThrowIfInvalid();
        var (_, decimals) = await cash.CurrencyAsync(ct).ConfigureAwait(false);
        var amount = CashSupport.Amount(r.Amount, decimals);
        var date = await cash.DateAsync(r.ReceiptDate, "receiptDate", ct).ConfigureAwait(false);
        // Revision 6: allocations to one or more invoices; "InvoiceId" alone = one invoice for the whole amount (as before).
        var requested = r.Allocations is { Count: > 0 } list ? list.Select((a, i) => (Field: $"allocations[{i}]", a.InvoiceId, a.Amount)).ToList()
            : string.IsNullOrWhiteSpace(r.InvoiceId) ? [] : [(Field: "invoiceId", r.InvoiceId, Amount: (decimal?)amount)];
        if (requested.Count > 200) throw Fail.Field("allocations", ErrorCodes.ValidationRange, "allocations");
        // On account: an active customer (7.4.1); against an invoice an archived customer can still pay it (DT-97).
        var customer = requested.Count == 0
            ? await support.ChooseAsync<Customer>("customerId", r.CustomerId, null, c => c.Name, ct).ConfigureAwait(false)
            : await cash.PartyAsync<Customer>("customerId", r.CustomerId, ct).ConfigureAwait(false);
        var allocations = new List<(Invoice Invoice, decimal Amount, string Field)>();
        foreach (var (field, invoiceToken, allocated) in requested)
        {
            var invoiceField = field == "invoiceId" ? field : field + ".invoiceId";
            var pid = support.Decode<Invoice>(invoiceToken) ?? throw Fail.Field(invoiceField, ErrorCodes.ValidationFormat, invoiceField);
            var inv = await db.Set<Invoice>().SingleOrDefaultAsync(i => i.PublicId == pid, ct).ConfigureAwait(false) ?? throw Fail.Field(invoiceField, ErrorCodes.ValidationFormat, invoiceField);
            if (allocations.Any(a => a.Invoice.Id == inv.Id)) throw Fail.RuleOn(invoiceField, ErrorCodes.ReceiptAllocationDuplicate, inv.Number);
            var share = CashSupport.Amount(allocated, decimals, field == "invoiceId" ? "amount" : field + ".amount");
            await locks.EnsureNotLockedByOthersAsync(InvoiceService.EntityType, inv.Id, ct).ConfigureAwait(false);
            allocations.Add((inv, share, field));
        }
        var allocatedTotal = allocations.Sum(a => a.Amount);
        if (allocatedTotal > amount) throw Fail.RuleOn("allocations", ErrorCodes.ReceiptAllocationsExceedAmount, Money.Format(allocatedTotal), Money.Format(amount));
        var money = await cash.ChooseAsync(r.PaymentMethod, r.CashBoxId, r.BankAccountId, r.Reference, ct).ConfigureAwait(false);

        var receipt = new Receipt
        {
            CustomerId = customer.Id, InvoiceId = allocations.Count == 1 ? allocations[0].Invoice.Id : null, ReceiptDate = date, Amount = amount, PaymentMethod = money.Method, CashBoxId = money.CashBoxId,
            BankAccountId = money.BankAccountId, Reference = money.Reference, AttachmentsCount = r.AttachmentsCount ?? 0, Notes = notes, Status = CashCodes.Active,
        };
        if (clientId is Guid id) receipt.PublicId = id;
        Receipt? raced = null;
        try
        {
            await db.InTransactionAsync(async c =>
            {
                await posting.EnsurePostingReadyAsync(c).ConfigureAwait(false);
                await periods.EnsureOpenAsync(date, c).ConfigureAwait(false);
                // The receipt number serialises receipts of the company; the invoice is read again under it.
                receipt.Number = await numbering.NextAsync(Configuration.DocumentTypes.Receipt, c).ConfigureAwait(false);
                foreach (var (inv, share, field) in allocations)
                {
                    await db.Set<Invoice>().Entry(inv).ReloadAsync(c).ConfigureAwait(false);
                    var invoiceField = field == "invoiceId" ? field : field + ".invoiceId";
                    if (inv.CustomerId != customer.Id || inv.Status != InvoiceCodes.Approved || inv.RemainingAmount <= 0)
                        throw Fail.RuleOn(invoiceField, ErrorCodes.ReceiptInvoiceNotOpen, inv.Number);
                    if (share > inv.RemainingAmount)
                        throw Fail.RuleOn(field == "invoiceId" ? "amount" : field + ".amount", ErrorCodes.ReceiptExceedsRemaining, Money.Format(share), Money.Format(inv.RemainingAmount));
                    inv.PaidAmount += share;
                    InvoiceService.Rebalance(inv);
                    changes.SetAction(inv, "Receipt", receipt.Number);
                    receipt.Allocations.Add(new ReceiptAllocation { InvoiceId = inv.Id, Amount = share });
                }
                db.Set<Receipt>().Add(receipt);
                if (await support.SaveCreateAsync<Receipt>(clientId, c).ConfigureAwait(false) is { } other) throw new RacedCreate(other);
                var entry = await posting.PostOrPendAsync(BuildEntry(receipt, money.AccountId, InvoiceText(allocations.Select(a => a.Invoice.Number))), c).ConfigureAwait(false);
                receipt.JournalEntryId = entry?.Id;
                await db.SaveChangesAsync(c).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (RacedCreate rc)
        {
            db.ChangeTracker.Clear();
            raced = rc.Existing;
        }
        return raced is not null ? (await DetailsAsync(raced.Id, ct).ConfigureAwait(false), false) : (await DetailsAsync(receipt.Id, ct).ConfigureAwait(false), true);
    }

    private sealed class RacedCreate(Receipt existing) : Exception
    {
        public Receipt Existing { get; } = existing;
    }

    /// <summary>Void (FR-069, rcpt.manage): reason, reversal dated like the receipt, invoice balance restored, number kept.</summary>
    public async Task<ReceiptDetails> VoidAsync(string token, CashVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var receipt = await support.FindAsync<Receipt>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, receipt.Id, ct).ConfigureAwait(false);
        CashSupport.EnsureActive(receipt, ErrorCodes.ReceiptIsVoid);
        var allocated = await db.Set<ReceiptAllocation>().Where(a => a.ReceiptId == receipt.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var a in allocated) await locks.EnsureNotLockedByOthersAsync(InvoiceService.EntityType, a.InvoiceId, ct).ConfigureAwait(false);
        CashSupport.RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(receipt, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(receipt.ReceiptDate, c).ConfigureAwait(false);
            receipt.Status = CashCodes.Void;
            receipt.VoidReason = why;
            receipt.VoidedBy = cash.UserId;
            receipt.VoidedAt = DateTime.UtcNow;
            changes.SetAction(receipt, "Void", why);
            // Every invoice the receipt was applied to gets its allocated amount back (the allocation rows stay as the record).
            foreach (var a in allocated)
            {
                var inv = await db.Set<Invoice>().SingleAsync(i => i.Id == a.InvoiceId, c).ConfigureAwait(false);
                inv.PaidAmount -= a.Amount;
                InvoiceService.Rebalance(inv);
                changes.SetAction(inv, "ReceiptVoid", receipt.Number);
            }
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await ReverseAsync(receipt, cash.En ? $"Void of receipt {receipt.Number}" : $"إلغاء سند القبض {receipt.Number}", c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(receipt.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Reverses the posted entry of a cash document, or closes its waiting pending posting (shared by the cash services).</summary>
    internal async Task ReverseAsync(Domain.Common.ICashDocument d, string description, CancellationToken ct) =>
        await CashPosting.ReverseAsync(db, posting, d, SourceType, description, ct).ConfigureAwait(false);

    /// <summary>The invoice numbers of a receipt for its entry text: up to three, then "…".</summary>
    internal static string? InvoiceText(IEnumerable<string?> numbers)
    {
        var list = numbers.Where(n => !string.IsNullOrEmpty(n)).ToList();
        return list.Count == 0 ? null : string.Join(", ", list.Take(3)) + (list.Count > 3 ? ", …" : string.Empty);
    }

    /// <summary>Receipt entry (8.6): Dr cash / bank, Cr trade receivables with the customer.</summary>
    public PostingRequest BuildEntry(Receipt r, long moneyAccountId, string? invoiceNumber)
    {
        ArgumentNullException.ThrowIfNull(r);
        var party = new PostingParty(AccountingCodes.ModuleTrn, AccountingCodes.PartyCustomer, r.CustomerId);
        var text = (cash.En ? "Receipt " : "سند قبض ") + r.Number + (invoiceNumber is null ? string.Empty : " · " + invoiceNumber);
        return new PostingRequest(r.ReceiptDate, text, AccountingCodes.ModuleTrn, SourceType, r.Id, false,
            [CashSupport.Debit(moneyAccountId, r.Amount), PostingLine.CreditKey(PostingKeys.TradeReceivables, r.Amount, party)]);
    }

    // ============================================================ reads (FR-071)

    internal sealed record Row(Receipt R, Guid CustomerPid, string CustomerName, Guid? InvoicePid, string? InvoiceNumber, Guid? BoxPid, string? BoxName, Guid? BankPid,
        string? BankName, Guid? EntryPid, string? EntryNumber, bool Pending);

    private IQueryable<Row> Rows(IQueryable<Receipt> q) => q.Select(r => new Row(r, r.Customer.PublicId, r.Customer.Name,
        r.Invoice != null ? r.Invoice.PublicId : null, r.Invoice != null ? r.Invoice.Number : null,
        r.CashBox != null ? r.CashBox.PublicId : null, r.CashBox != null ? r.CashBox.Name : null,
        r.BankAccount != null ? r.BankAccount.PublicId : null, r.BankAccount != null ? r.BankAccount.Name : null,
        r.JournalEntry != null ? r.JournalEntry.PublicId : null, r.JournalEntry != null ? r.JournalEntry.Number : null,
        db.Set<PendingPosting>().Any(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == SourceType && p.SourceId == r.Id && p.ResolvedAt == null)));

    private static readonly IReadOnlyDictionary<string, Expression<Func<Receipt, object?>>> SortFields = new Dictionary<string, Expression<Func<Receipt, object?>>>(StringComparer.Ordinal)
    {
        ["receiptDate"] = r => r.ReceiptDate,
        ["number"] = r => r.Number,
        ["customer"] = r => r.Customer.Name,
        ["amount"] = r => r.Amount,
    };

    private IQueryable<Receipt> Filter(ReceiptListQuery q)
    {
        var query = CashSupport.WithStatus(db.Set<Receipt>().AsNoTracking(), q.Status);
        if (!string.IsNullOrWhiteSpace(q.Customer))
        {
            var g = support.Decode<Customer>(q.Customer) ?? throw Fail.Field("customer", ErrorCodes.ValidationFormat, "customer");
            query = query.Where(r => r.Customer.PublicId == g);
        }
        if (q.From is DateOnly from) query = query.Where(r => r.ReceiptDate >= from);
        if (q.To is DateOnly to) query = query.Where(r => r.ReceiptDate <= to);
        if (CashSupport.Method(q.Method) is { } m) query = query.Where(r => r.PaymentMethod == m);
        if (!string.IsNullOrWhiteSpace(q.CashBox))
        {
            var g = support.Decode<CashBox>(q.CashBox) ?? throw Fail.Field("cashBox", ErrorCodes.ValidationFormat, "cashBox");
            query = query.Where(r => r.CashBox != null && r.CashBox.PublicId == g);
        }
        if (!string.IsNullOrWhiteSpace(q.BankAccount))
        {
            var g = support.Decode<BankAccount>(q.BankAccount) ?? throw Fail.Field("bankAccount", ErrorCodes.ValidationFormat, "bankAccount");
            query = query.Where(r => r.BankAccount != null && r.BankAccount.PublicId == g);
        }
        query = (q.Applied?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "invoice" => query.Where(r => r.Allocations.Any()),
            "account" => query.Where(r => !r.Allocations.Any()),
            // Revision 6 part B (accounting dashboard reminder): any part not allocated to invoices.
            "unallocated" => query.Where(r => r.Amount > (r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0)),
            _ => throw Fail.Field("applied", ErrorCodes.ValidationFormat, "applied"),
        };
        if (!string.IsNullOrWhiteSpace(q.Invoice))
        {
            var g = support.Decode<Invoice>(q.Invoice) ?? throw Fail.Field("invoice", ErrorCodes.ValidationFormat, "invoice");
            query = query.Where(r => r.Allocations.Any(a => a.Invoice.PublicId == g));
        }
        if (MasterDataSupport.SearchText(q.Q) is { } text)
            query = query.Where(r => r.Number.Contains(text) || (r.Reference != null && r.Reference.Contains(text)) || r.Allocations.Any(a => a.Invoice.Number!.Contains(text)));
        return query;
    }

    public async Task<ReceiptPage> ListAsync(ReceiptListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = Filter(q);
        var paging = new PageRequest(q.Page, q.PageSize);
        var totals = await query.GroupBy(_ => 1).Select(g => new CashListTotals(g.Count(), g.Sum(r => r.Status == CashCodes.Active ? r.Amount : 0), g.Count(r => r.Status == CashCodes.Active))).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? new CashListTotals(0, 0);
        var rows = await Rows(MasterDataSupport.Sort(query, q.Sort, SortFields, "-receiptDate").ThenByDescending(r => r.Id).Skip(paging.Skip).Take(paging.SafePageSize))
            .ToListAsync(ct).ConfigureAwait(false);
        var allocations = await AllocationsAsync(rows.Select(x => x.R.Id).ToList(), ct).ConfigureAwait(false);
        return new ReceiptPage(new PagedResult<ReceiptResponse>(rows.Select(x => ToResponse(x, allocations)).ToList(), paging.SafePage, paging.SafePageSize, totals.Count), totals);
    }

    public async Task<GeneratedFile> ExportAsync(ReceiptListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var rows = await Rows(MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "-receiptDate").ThenByDescending(r => r.Id).Take(SheetData.MaxRows)).ToListAsync(ct).ConfigureAwait(false);
        var allocations = await AllocationsAsync(rows.Select(x => x.R.Id).ToList(), ct).ConfigureAwait(false);
        string[] headers = cash.En
            ? ["Number", "Date", "Customer", "Invoice", "Amount", "Method", "Cash box / bank", "Reference", "Attachments", "Status", "Notes"]
            : ["الرقم", "التاريخ", "العميل", "الفاتورة", "المبلغ", "طريقة الدفع", "الصندوق / البنك", "المرجع", "المرفقات", "الحالة", "ملاحظات"];
        var data = rows.Select(x => (IReadOnlyList<object?>)[x.R.Number, x.R.ReceiptDate, x.CustomerName,
            allocations.TryGetValue(x.R.Id, out var a) && a.Count > 0 ? string.Join(", ", a.Select(i => i.Invoice.Number)) : (cash.En ? "On account" : "على الحساب"), x.R.Amount,
            x.R.PaymentMethod, x.BoxName ?? x.BankName, x.R.Reference, x.R.AttachmentsCount, x.R.Status, x.R.Notes]).ToList();
        return await CashExport.FileAsync("receipts", format, headers, data, sheets, exporter, ct).ConfigureAwait(false);
    }

    public async Task<ReceiptDetails> GetAsync(string token, CancellationToken ct)
    {
        var r = await support.FindAsync<Receipt>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(r.Id, ct).ConfigureAwait(false);
    }

    internal async Task<ReceiptDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var row = await Rows(db.Set<Receipt>().AsNoTracking().Where(r => r.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        var allocations = await AllocationsAsync([id], ct).ConfigureAwait(false);
        return new ReceiptDetails(ToResponse(row, allocations), await cash.WordsAsync(row.R.Amount, ct).ConfigureAwait(false), await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false));
    }

    /// <summary>The allocations of the given receipts, by receipt, in invoice-date order.</summary>
    private async Task<Dictionary<long, List<ReceiptAllocationResponse>>> AllocationsAsync(IReadOnlyCollection<long> receiptIds, CancellationToken ct) =>
        (await db.Set<ReceiptAllocation>().AsNoTracking().Where(a => receiptIds.Contains(a.ReceiptId))
            .OrderBy(a => a.Invoice.InvoiceDate).ThenBy(a => a.Invoice.Number)
            .Select(a => new { a.ReceiptId, a.Invoice.PublicId, a.Invoice.Number, a.Amount }).ToListAsync(ct).ConfigureAwait(false))
        .GroupBy(a => a.ReceiptId)
        .ToDictionary(g => g.Key, g => g.Select(a => new ReceiptAllocationResponse(new DocRef(support.Token<Invoice>(a.PublicId), a.Number), a.Amount)).ToList());

    private ReceiptResponse ToResponse(Row x, Dictionary<long, List<ReceiptAllocationResponse>> allocations)
    {
        var own = allocations.GetValueOrDefault(x.R.Id) ?? [];
        return new(support.Token<Receipt>(x.R.PublicId), x.R.Number, new Ref(support.Token<Customer>(x.CustomerPid), x.CustomerName),
            x.InvoicePid is Guid i ? new DocRef(support.Token<Invoice>(i), x.InvoiceNumber) : null, x.R.ReceiptDate, x.R.Amount, x.R.PaymentMethod,
            cash.Account(x.BoxPid, x.BoxName, x.BankPid, x.BankName), x.R.Reference, x.R.AttachmentsCount, x.R.Notes, x.R.Status, cash.Entry(x.EntryPid, x.EntryNumber),
            x.Pending, x.R.VoidReason, DateTime.SpecifyKind(x.R.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(x.R.RowVersion), own, x.R.Amount - own.Sum(a => a.Amount));
    }
}

/// <summary>Pending-posting retries for receipts.</summary>
public sealed class ReceiptPostingHandler(IAppDbContext db, IUrlTokenService tokens, ReceiptService receipts, CashSupport cash) : CashPostingHandler<Receipt>(db, tokens)
{
    private readonly IAppDbContext _db = db;

    public override string SourceType => ReceiptService.SourceType;

    protected override string VoidCode => ErrorCodes.ReceiptIsVoid;

    protected override async Task<PostingRequest?> EntryAsync(Receipt d, CancellationToken ct)
    {
        var numbers = await _db.Set<ReceiptAllocation>().Where(a => a.ReceiptId == d.Id).OrderBy(a => a.Invoice.InvoiceDate).Select(a => a.Invoice.Number).ToListAsync(ct).ConfigureAwait(false);
        return receipts.BuildEntry(d, await cash.AccountIdAsync(d.CashBoxId, d.BankAccountId, ct).ConfigureAwait(false), ReceiptService.InvoiceText(numbers));
    }
}

/// <summary>Reversal of cash documents on void or edit (BR-035): the posted entry is reversed; a waiting pending posting is closed.</summary>
internal static class CashPosting
{
    public static async Task ReverseAsync(IAppDbContext db, PostingEngine posting, Domain.Common.ICashDocument d, string sourceType, string description, CancellationToken ct,
        string module = AccountingCodes.ModuleTrn)
    {
        if (d.JournalEntryId is long entryId)
        {
            var entry = await db.Set<JournalEntry>().SingleAsync(e => e.Id == entryId, ct).ConfigureAwait(false);
            if (entry.Status == AccountingCodes.Posted) await posting.ReverseAsync(entry, description, null, ct).ConfigureAwait(false);
        }
        foreach (var p in await db.Set<PendingPosting>().Where(p => p.SourceModule == module && p.SourceType == sourceType && p.SourceId == d.Id && p.ResolvedAt == null)
                     .ToListAsync(ct).ConfigureAwait(false))
            p.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>Excel or CSV export of a cash or AP/AR list, or its printout (PDF / HTML, FR-150).</summary>
internal static class CashExport
{
    /// <summary>Excel or CSV only (callers with their own printout).</summary>
    public static GeneratedFile File(string name, string? format, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, ISpreadsheetService sheets) =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? new GeneratedFile(name + ".csv", "text/csv", sheets.WriteCsv(headers, rows))
            : new GeneratedFile(name + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sheets.WriteXlsx(name, headers, rows));

    public static async Task<GeneratedFile> FileAsync(string name, string? format, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, ISpreadsheetService sheets,
        Reports.ReportExporter exporter, CancellationToken ct)
    {
        if (Reports.ReportExporter.IsPrintout(format)) return await exporter.ListPrintoutAsync(Title(name, exporter.En), name, headers, rows, format!, ct).ConfigureAwait(false);
        return File(name, format, headers, rows, sheets);
    }

    private static string Title(string name, bool en) => name switch
    {
        "receipts" => en ? "Receipts" : "سندات القبض",
        "advances" => en ? "Driver advances" : "سلف السائقين",
        "partner-payments" => en ? "Partner payments" : "دفعات الشركاء",
        "supplier-bills" => en ? "Supplier bills" : "فواتير الموردين",
        "receivables" => en ? "Receivable documents" : "مستندات الذمم المدينة",
        "supplier-payments" => en ? "Supplier payments" : "دفعات الموردين",
        "collections" => en ? "Collections" : "التحصيلات",
        "payable-returns" => en ? "Payable returns" : "مرتجعات الموردين",
        "receivable-returns" => en ? "Receivable returns" : "مرتجعات العملاء",
        _ => name,
    };
}
