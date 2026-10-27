using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Invoicing;

/// <summary>Company figures used by invoices: tax rate and mode (BR-022), currency decimals, today.</summary>
public sealed record InvoiceCompany(decimal TaxRate, bool TaxInclusive, int Decimals, string CurrencyCode, DateOnly Today);

/// <summary>
/// Customer invoices (FR-038 … FR-048, PRD 7.3, 8.3): trip picker; trip-based and manual drafts that reserve their trips (FR-042);
/// approval with the next gap-free number, the revenue entry and invoiced trips (FR-043, FR-044); the invoice-approval setting
/// (FR-041); void with reversal, number kept and trips released (FR-045, FR-046); list with filters and totals (FR-047); view (FR-048).
/// </summary>
public sealed class InvoiceService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks,
    IPeriodGuard periods, INumberingService numbering, PostingEngine posting, CompanySettingsService settings, TripService trips, EInvoicing.EInvoiceService einvoice)
{
    public const string EntityType = "TRN.Invoice";
    public const string SourceType = "Invoice";

    private bool En => ctx.Language == "en";

    public async Task<InvoiceCompany> CompanyAsync(CancellationToken ct)
    {
        var c = await db.Set<Company>().AsNoTracking().Where(x => x.Id == ctx.CompanyId).Select(x => new { x.TaxRate, x.TaxInclusive, x.CurrencyDecimals, x.CurrencyCode })
            .SingleAsync(ct).ConfigureAwait(false);
        return new InvoiceCompany(c.TaxRate, c.TaxInclusive, c.CurrencyDecimals > 0 ? c.CurrencyDecimals : 3, c.CurrencyCode, await support.TodayAsync(ct).ConfigureAwait(false));
    }

    // ============================================================ trip picker (FR-038)

    public async Task<UnbilledTripsResponse> UnbilledAsync(UnbilledTripsQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var customer = support.Decode<Customer>(q.Customer) ?? throw Fail.Field("customer", ErrorCodes.ValidationRequired, new FieldRef("customer"));
        var query = db.Set<Trip>().AsNoTracking().Where(t => t.Customer.PublicId == customer && t.Status == TripService.Active && t.InvoiceId == null);
        if (q.From is DateOnly from) query = query.Where(t => t.TripDate >= from);
        if (q.To is DateOnly to) query = query.Where(t => t.TripDate <= to);
        if (Pid<LoadingSite>(q.LoadingSite, "loadingSite") is Guid l) query = query.Where(t => t.LoadingSite.PublicId == l);
        if (Pid<UnloadingSite>(q.UnloadingSite, "unloadingSite") is Guid u) query = query.Where(t => t.UnloadingSite.PublicId == u);
        if (Pid<Material>(q.Material, "material") is Guid m) query = query.Where(t => t.Material.PublicId == m);
        var rows = await trips.Rows(query.OrderBy(t => t.TripDate).ThenBy(t => t.Id).Take(5000)).ToListAsync(ct).ConfigureAwait(false);
        var open = await trips.PartnerOpenAsync(rows, ct).ConfigureAwait(false);
        var list = rows.Select(r => trips.ToResponse(r, open)).ToList();
        return new UnbilledTripsResponse(list, list.Count, list.Sum(t => t.BillingQty), list.Sum(t => t.Revenue));
    }

    private Guid? Pid<T>(string? token, string field) =>
        string.IsNullOrWhiteSpace(token) ? null : support.Decode<T>(token) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, new FieldRef(field));

    // ============================================================ drafts and approval

    public async Task<(InvoiceDetails Value, bool Created)> CreateAsync(InvoiceRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Invoice>(clientId, ct).ConfigureAwait(false) is { } again) return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var approve = r.Approve || !await ApprovalOnAsync(ct).ConfigureAwait(false);
        var inv = new Invoice { Status = InvoiceCodes.Draft };
        if (clientId is Guid id) inv.PublicId = id;
        var raced = await db.InTransactionAsync(async c =>
        {
            var plan = await PlanAsync(inv, r, company, isNew: true, c).ConfigureAwait(false);
            db.Set<Invoice>().Add(inv);
            if (await support.SaveCreateAsync<Invoice>(clientId, c).ConfigureAwait(false) is { } other) return other;
            await ApplyLinesAsync(inv, plan, c).ConfigureAwait(false);
            if (approve) await ApproveCoreAsync(inv, c).ConfigureAwait(false);
            return null;
        }, ct).ConfigureAwait(false);
        return raced is not null ? (await DetailsAsync(raced.Id, ct).ConfigureAwait(false), false) : (await DetailsAsync(inv.Id, ct).ConfigureAwait(false), true);
    }

    /// <summary>Edit a draft (FR-042): trips, lines, dates, discount, fees, layout; Save &amp; approve approves in the same step.</summary>
    public async Task<InvoiceDetails> UpdateDraftAsync(string token, InvoiceRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var inv = await support.FindAsync<Invoice>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, inv.Id, ct).ConfigureAwait(false);
        if (inv.Status != InvoiceCodes.Draft) throw Fail.Rule(ErrorCodes.InvoiceNotDraft, inv.Number);
        RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(inv, r.RowVersion);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var approve = r.Approve || !await ApprovalOnAsync(ct).ConfigureAwait(false);
        await db.InTransactionAsync(async c =>
        {
            var plan = await PlanAsync(inv, r, company, isNew: false, c).ConfigureAwait(false);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await ApplyLinesAsync(inv, plan, c).ConfigureAwait(false);
            if (approve) await ApproveCoreAsync(inv, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(inv.Id, ct).ConfigureAwait(false);
    }

    public async Task<InvoiceDetails> ApproveAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var inv = await support.FindAsync<Invoice>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, inv.Id, ct).ConfigureAwait(false);
        if (inv.Status != InvoiceCodes.Draft) throw Fail.Rule(ErrorCodes.InvoiceNotDraft, inv.Number);
        RequireRowVersion(rowVersion);
        db.ExpectRowVersion(inv, rowVersion);
        await db.InTransactionAsync(c => ApproveCoreAsync(inv, c), ct).ConfigureAwait(false);
        return await DetailsAsync(inv.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Soft-deletes a draft with a reason; its trips are released (FR-042).</summary>
    public async Task DeleteDraftAsync(string token, string? reason, string? rowVersion, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var inv = await support.FindAsync<Invoice>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, inv.Id, ct).ConfigureAwait(false);
        if (inv.Status != InvoiceCodes.Draft) throw Fail.Rule(ErrorCodes.InvoiceNotDraft, inv.Number);
        RequireRowVersion(rowVersion);
        db.ExpectRowVersion(inv, rowVersion);
        await db.InTransactionAsync(async c =>
        {
            await ReleaseTripsAsync(inv, null, "RemovedFromInvoice", c).ConfigureAwait(false);
            foreach (var line in await db.Set<InvoiceLine>().Where(l => l.InvoiceId == inv.Id).ToListAsync(c).ConfigureAwait(false))
            {
                line.DeleteReason = why;
                db.Set<InvoiceLine>().Remove(line);
            }
            inv.DeleteReason = why;
            db.Set<Invoice>().Remove(inv);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Live totals of the invoice form (UX-17; the rules stay on the server, NFR-14): the same line amounts, discount, fees and tax as a save,
    /// the due date from the customer's terms and the approval setting. Nothing is saved; unknown trips are left out.
    /// </summary>
    public async Task<InvoiceCalculationResponse> CalculateAsync(InvoiceCalculationRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        decimal linesTotal;
        if (string.Equals(r.InvoiceType, InvoiceCodes.Manual, StringComparison.OrdinalIgnoreCase))
        {
            linesTotal = (r.Lines ?? []).Take(500).Where(l => l.Quantity is > 0 && l.UnitPrice is > 0)
                .Sum(l => InvoiceCalculator.LineAmount(l.Quantity!.Value, l.UnitPrice!.Value, company.Decimals));
        }
        else
        {
            var ids = (r.TripIds ?? []).Take(5000).Select(t => support.Decode<Trip>(t)).OfType<Guid>().Distinct().ToList();
            linesTotal = ids.Count == 0 ? 0m : await db.Set<Trip>().AsNoTracking().Where(t => ids.Contains(t.PublicId) && t.Status == TripService.Active)
                .SumAsync(t => t.Revenue, ct).ConfigureAwait(false);
        }
        var discount = Math.Max(0m, r.Discount ?? 0m);
        var fees = Math.Max(0m, r.ExtraFees ?? 0m);
        var totals = InvoiceCalculator.Invoice(linesTotal, Math.Min(discount, linesTotal), fees, company.TaxRate, company.TaxInclusive, company.Decimals);
        int? terms = null;
        if (support.Decode<Customer>(r.CustomerId) is Guid c)
            terms = await db.Set<Customer>().AsNoTracking().Where(x => x.PublicId == c).Select(x => (int?)x.PaymentTermsDays).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var due = terms is int days && r.InvoiceDate is DateOnly d ? d.AddDays(days) : (DateOnly?)null;
        return new InvoiceCalculationResponse(totals.Subtotal, totals.Discount, totals.ExtraFees, totals.BaseAmount, company.TaxRate, totals.Tax, totals.Total,
            company.TaxInclusive, company.Decimals, company.Today, due, terms, await ApprovalOnAsync(ct).ConfigureAwait(false));
    }

    private async Task<bool> ApprovalOnAsync(CancellationToken ct) =>
        !string.Equals(await settings.SettingAsync(SettingKeys.InvoiceApproval, ct).ConfigureAwait(false) ?? SettingKeys.Defaults[SettingKeys.InvoiceApproval], "false", StringComparison.OrdinalIgnoreCase);

    private static void RequireRowVersion(string? rowVersion)
    {
        if (string.IsNullOrEmpty(rowVersion)) throw Fail.Field("rowVersion", ErrorCodes.ValidationRequired, new FieldRef("rowVersion"));
    }

    /// <summary>The validated content of a request: header values on the invoice, the trips or manual lines to write.</summary>
    private sealed record Plan(IReadOnlyList<Trip> Trips, IReadOnlyList<(string Description, decimal Quantity, decimal UnitPrice, decimal Amount)> Manual);

    private async Task<Plan> PlanAsync(Invoice inv, InvoiceRequest r, InvoiceCompany company, bool isNew, CancellationToken ct)
    {
        var type = (r.InvoiceType?.Trim()) switch
        {
            null or "" => isNew ? null : inv.InvoiceType,
            var x when string.Equals(x, InvoiceCodes.Trip, StringComparison.OrdinalIgnoreCase) => InvoiceCodes.Trip,
            var x when string.Equals(x, InvoiceCodes.Manual, StringComparison.OrdinalIgnoreCase) => InvoiceCodes.Manual,
            _ => throw Fail.Field("invoiceType", ErrorCodes.ValidationFormat, new FieldRef("invoiceType")),
        };
        var layout = (r.LayoutType?.Trim()) switch
        {
            null or "" => isNew ? InvoiceCodes.Claim : inv.LayoutType,   // rev. 5 / D-02 default; an edit keeps the stored layout (review L1)
            var x when string.Equals(x, InvoiceCodes.Claim, StringComparison.OrdinalIgnoreCase) => InvoiceCodes.Claim,
            var x when string.Equals(x, InvoiceCodes.TaxInvoice, StringComparison.OrdinalIgnoreCase) => InvoiceCodes.TaxInvoice,
            _ => throw Fail.Field("layoutType", ErrorCodes.ValidationFormat, new FieldRef("layoutType")),
        };
        var notes = TextNormalizer.CleanText(r.Notes);
        var feesText = TextNormalizer.CleanText(r.ExtraFeesDescription);
        var discount = r.Discount ?? 0m;
        var fees = r.ExtraFees ?? 0m;
        new Validator()
            .Check(type is not null, "invoiceType", ErrorCodes.ValidationRequired)
            .Check(!string.IsNullOrWhiteSpace(r.CustomerId), "customerId", ErrorCodes.ValidationRequired)
            .Check(r.InvoiceDate is not null, "invoiceDate", ErrorCodes.ValidationRequired)
            .Check(r.InvoiceDate is null || r.InvoiceDate <= company.Today, "invoiceDate", ErrorCodes.DateFuture)
            .Check(r.DueDate is null || r.InvoiceDate is null || r.DueDate >= r.InvoiceDate, "dueDate", ErrorCodes.ValidationRange)
            .Check(Money.IsValidAmount(discount, company.Decimals), "discount", ErrorCodes.ValidationRange)
            .Check(Money.IsValidAmount(fees, company.Decimals), "extraFees", ErrorCodes.ValidationRange)
            .Check(fees == 0 || feesText is not null, "extraFeesDescription", ErrorCodes.ValidationRequired)
            .MaxLength("extraFeesDescription", feesText, 200)
            .MaxLength("notes", notes, 1000)
            .ThrowIfInvalid();
        var customer = await support.ChooseAsync<Customer>("customerId", r.CustomerId, isNew ? null : inv.CustomerId, x => x.Name, ct).ConfigureAwait(false);
        await periods.EnsureOpenAsync(r.InvoiceDate!.Value, ct).ConfigureAwait(false);

        var tripList = new List<Trip>();
        var manual = new List<(string, decimal, decimal, decimal)>();
        if (type == InvoiceCodes.Trip)
        {
            var tokens = (r.TripIds ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal).ToList();
            if (tokens.Count == 0) throw Fail.Field("tripIds", ErrorCodes.ValidationRequired, new FieldRef("tripIds"));
            if (tokens.Count > 5000) throw Fail.Field("tripIds", ErrorCodes.ValidationRange, new FieldRef("tripIds"));
            var ids = tokens.Select(t => support.Decode<Trip>(t) ?? throw Fail.Field("tripIds", ErrorCodes.ValidationFormat, new FieldRef("tripIds"))).ToList();
            tripList = await db.Set<Trip>().Where(t => ids.Contains(t.PublicId)).ToListAsync(ct).ConfigureAwait(false);
            if (tripList.Count != ids.Count) throw Fail.Field("tripIds", ErrorCodes.ValidationFormat, new FieldRef("tripIds"));
            foreach (var t in tripList)
            {
                // Only the customer's active trips not on another invoice (FR-038, FR-044).
                if (t.CustomerId != customer.Id || t.Status != TripService.Active || (t.InvoiceId is long other && other != inv.Id))
                    throw Fail.Rule(ErrorCodes.InvoiceTripUnavailable, t.ReceiptNo);
            }
        }
        else
        {
            var lines = r.Lines ?? [];
            if (lines.Count == 0) throw Fail.Field("lines", ErrorCodes.ValidationRequired, new FieldRef("lines"));
            if (lines.Count > 500) throw Fail.Field("lines", ErrorCodes.ValidationRange, new FieldRef("lines"));
            var v = new Validator();
            for (var i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                var d = TextNormalizer.CleanText(l.Description);
                v.Check(d is not null, $"lines[{i}].description", ErrorCodes.ValidationRequired).MaxLength($"lines[{i}].description", d, 300)
                 .Check(l.Quantity is decimal q && q > 0 && decimal.Round(q, 3) == q && q < 1_000_000_000m, $"lines[{i}].quantity", ErrorCodes.ValidationRange)
                 .Check(l.UnitPrice is decimal p && p > 0 && Money.IsValidAmount(p, 4), $"lines[{i}].unitPrice", ErrorCodes.ValidationRange);
                if (d is not null && l.Quantity is decimal qq && l.UnitPrice is decimal pp) manual.Add((d, qq, pp, InvoiceCalculator.LineAmount(qq, pp, company.Decimals)));
            }
            v.ThrowIfInvalid();
        }

        var linesTotal = type == InvoiceCodes.Trip ? tripList.Sum(t => t.Revenue) : manual.Sum(m => m.Item4);
        if (discount > linesTotal) throw Fail.Field("discount", ErrorCodes.ValidationRange, new FieldRef("discount"));
        var totals = InvoiceCalculator.Invoice(linesTotal, discount, fees, company.TaxRate, company.TaxInclusive, company.Decimals);
        var terms = await db.Set<Customer>().AsNoTracking().Where(c => c.Id == customer.Id).Select(c => c.PaymentTermsDays).SingleAsync(ct).ConfigureAwait(false);

        inv.InvoiceType = type!;
        inv.CustomerId = customer.Id;
        inv.InvoiceDate = r.InvoiceDate.Value;
        inv.DueDate = r.DueDate ?? r.InvoiceDate.Value.AddDays(terms);
        inv.Subtotal = totals.Subtotal;
        inv.Discount = totals.Discount;
        inv.ExtraFees = totals.ExtraFees;
        inv.ExtraFeesDescription = fees == 0 ? null : feesText;
        inv.BaseAmount = totals.BaseAmount;
        inv.TaxRate = company.TaxRate;
        inv.Tax = totals.Tax;
        inv.Total = totals.Total;
        inv.RemainingAmount = totals.Total;
        inv.LayoutType = layout;
        inv.Notes = notes;
        return new Plan(tripList, manual);
    }

    /// <summary>Rewrites the draft's lines and reserves its trips (FR-042); trips no longer listed are released.</summary>
    private async Task ApplyLinesAsync(Invoice inv, Plan plan, CancellationToken ct)
    {
        var keep = plan.Trips.Select(t => t.Id).ToHashSet();
        await ReleaseTripsAsync(inv, keep, "RemovedFromInvoice", ct).ConfigureAwait(false);
        foreach (var line in await db.Set<InvoiceLine>().Where(l => l.InvoiceId == inv.Id).ToListAsync(ct).ConfigureAwait(false))
        {
            line.DeleteReason = En ? "Draft lines rewritten" : "إعادة كتابة بنود المسودة";
            db.Set<InvoiceLine>().Remove(line);
        }
        var order = 0;
        foreach (var t in plan.Trips.OrderBy(t => t.TripDate).ThenBy(t => t.Id))
        {
            db.Set<InvoiceLine>().Add(new InvoiceLine { InvoiceId = inv.Id, TripId = t.Id, Quantity = t.BillingQty, UnitPrice = t.CustomerPrice, Amount = t.Revenue, SortOrder = ++order });
            if (t.InvoiceId != inv.Id)
            {
                t.InvoiceId = inv.Id;
                changes.SetAction(t, "AddedToInvoice", En ? "Draft invoice" : "مسودة فاتورة");
            }
        }
        foreach (var m in plan.Manual)
            db.Set<InvoiceLine>().Add(new InvoiceLine { InvoiceId = inv.Id, Description = m.Description, Quantity = m.Quantity, UnitPrice = m.UnitPrice, Amount = m.Amount, SortOrder = ++order });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Clears InvoiceId on the invoice's trips (except <paramref name="keep"/>), each recorded in the trip's history.</summary>
    private async Task ReleaseTripsAsync(Invoice inv, HashSet<long>? keep, string action, CancellationToken ct)
    {
        var onInvoice = await db.Set<Trip>().Where(t => t.InvoiceId == inv.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var t in onInvoice.Where(t => keep is null || !keep.Contains(t.Id)))
        {
            t.InvoiceId = null;
            changes.SetAction(t, action, inv.Number ?? (En ? "Draft invoice" : "مسودة فاتورة"));
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Approval (FR-043, FR-044): refused while account mapping is incomplete, the date is in a closed period, a trip is no longer
    /// active and on this invoice, or the total is not above 0. Assigns the next gap-free number, posts the revenue entry, marks the
    /// trips invoiced, e-invoicing queued with the next counter when enabled (FR-043, FR-061).
    /// </summary>
    private async Task ApproveCoreAsync(Invoice inv, CancellationToken ct)
    {
        await posting.EnsurePostingReadyAsync(ct).ConfigureAwait(false);
        if (inv.Total <= 0) throw Fail.Rule(ErrorCodes.InvoiceTotalZero);
        await periods.EnsureOpenAsync(inv.InvoiceDate, ct).ConfigureAwait(false);
        var tripLines = await db.Set<InvoiceLine>().AsNoTracking().Where(l => l.InvoiceId == inv.Id && l.TripId != null).Select(l => l.TripId!.Value).ToListAsync(ct).ConfigureAwait(false);
        var bad = await db.Set<Trip>().Where(t => tripLines.Contains(t.Id) && (t.Status != TripService.Active || t.InvoiceId != inv.Id)).Select(t => t.ReceiptNo).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (bad is not null) throw Fail.Rule(ErrorCodes.InvoiceTripUnavailable, bad);

        inv.Number = await numbering.NextAsync(Configuration.DocumentTypes.Invoice, ct).ConfigureAwait(false);
        inv.Status = InvoiceCodes.Approved;
        inv.ApprovedBy = ctx.UserId ?? throw new InvalidOperationException("Approval needs a user.");
        inv.ApprovedAt = DateTime.UtcNow;
        inv.RemainingAmount = inv.Total - inv.PaidAmount - inv.ReturnedAmount;
        changes.SetAction(inv, "Approve", inv.Number);
        // Each trip records "Invoiced" with the number; its row version advances, so stale trip edits are refused (DT-73).
        foreach (var t in await db.Set<Trip>().Where(t => tripLines.Contains(t.Id)).ToListAsync(ct).ConfigureAwait(false))
        {
            changes.SetAction(t, "Invoiced", inv.Number);
            db.Set<Trip>().Entry(t).State = EntityState.Modified;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var entry = await posting.PostOrPendAsync(BuildEntry(inv)!, ct).ConfigureAwait(false);
        inv.JournalEntryId = entry?.Id;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await einvoice.QueueInvoiceAsync(inv, ct).ConfigureAwait(false);
    }

    // ============================================================ void (FR-045, FR-046)

    public async Task<InvoiceDetails> VoidAsync(string token, InvoiceVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var inv = await support.FindAsync<Invoice>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, inv.Id, ct).ConfigureAwait(false);
        if (inv.Status == InvoiceCodes.Draft) throw Fail.Rule(ErrorCodes.InvoiceIsDraft);
        if (inv.Status == InvoiceCodes.Void) throw Fail.Rule(ErrorCodes.InvoiceIsVoid, inv.Number);
        RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(inv, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            // Blocked with active receipts or credit notes (FR-045, BR-040).
            var credits = await db.Set<CreditNote>().CountAsync(x => x.InvoiceId == inv.Id && x.Status == InvoiceCodes.Posted, c).ConfigureAwait(false);
            var receipts = await db.Set<ReceiptAllocation>().CountAsync(x => x.InvoiceId == inv.Id && x.Receipt.Status != InvoiceCodes.Void, c).ConfigureAwait(false);
            if (credits > 0 || receipts > 0 || inv.PaidAmount > 0 || inv.ReturnedAmount > 0) throw Fail.Rule(ErrorCodes.InvoiceHasPayments, inv.Number, receipts, credits);
            await periods.EnsureOpenAsync(inv.InvoiceDate, c).ConfigureAwait(false);
            // An invoice accepted by the national e-invoicing system is cancelled only by a full credit note (FR-046); a queued or
            // rejected submission is cancelled (waits for a submission in progress).
            if (!await einvoice.CancelAsync(inv.Id, null, why, c).ConfigureAwait(false))
                throw Fail.Rule(ErrorCodes.InvoiceEInvoiced, inv.Number);
            inv.Status = InvoiceCodes.Void;
            inv.VoidReason = why;
            inv.VoidedBy = ctx.UserId ?? throw new InvalidOperationException("A void needs a user.");
            inv.VoidedAt = DateTime.UtcNow;
            inv.RemainingAmount = 0;
            changes.SetAction(inv, "Void", why);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            if (inv.JournalEntryId is long entryId)
            {
                var entry = await db.Set<JournalEntry>().SingleAsync(e => e.Id == entryId, c).ConfigureAwait(false);
                if (entry.Status == AccountingCodes.Posted)
                    await posting.ReverseAsync(entry, $"{(En ? "Void of invoice" : "إلغاء الفاتورة")} {inv.Number}", null, c).ConfigureAwait(false);
            }
            await ResolvePendingAsync(SourceType, inv.Id, c).ConfigureAwait(false);
            await ReleaseTripsAsync(inv, null, "RemovedFromInvoice", c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(inv.Id, ct).ConfigureAwait(false);
    }

    internal async Task ResolvePendingAsync(string sourceType, long id, CancellationToken ct)
    {
        foreach (var p in await db.Set<PendingPosting>().Where(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == sourceType && p.SourceId == id && p.ResolvedAt == null)
                     .ToListAsync(ct).ConfigureAwait(false))
            p.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Remaining = total − active receipts − active credit notes; Closed at 0, Approved again above 0 (7.3.2).</summary>
    public static void Rebalance(Invoice inv)
    {
        ArgumentNullException.ThrowIfNull(inv);
        if (inv.Status is InvoiceCodes.Draft or InvoiceCodes.Void) return;
        inv.RemainingAmount = inv.Total - inv.PaidAmount - inv.ReturnedAmount;
        inv.Status = inv.RemainingAmount == 0 ? InvoiceCodes.Closed : InvoiceCodes.Approved;
    }

    // ============================================================ posting (8.6)

    /// <summary>
    /// Invoice entry: Dr trade receivables (total, party customer), Dr discount allowed (discount); Cr transport revenue (subtotal),
    /// Cr other revenue (fees), Cr sales tax output (tax). Manual invoices use the same keys.
    /// </summary>
    public PostingRequest? BuildEntry(Invoice inv)
    {
        ArgumentNullException.ThrowIfNull(inv);
        if (inv.Status is InvoiceCodes.Draft or InvoiceCodes.Void || inv.Number is null) return null;
        var party = new PostingParty(AccountingCodes.ModuleTrn, AccountingCodes.PartyCustomer, inv.CustomerId);
        var lines = new List<PostingLine> { PostingLine.DebitKey(PostingKeys.TradeReceivables, inv.Total, party) };
        if (inv.Discount > 0) lines.Add(PostingLine.DebitKey(PostingKeys.DiscountAllowed, inv.Discount));
        if (inv.Subtotal > 0) lines.Add(PostingLine.CreditKey(PostingKeys.TransportRevenue, inv.Subtotal));
        if (inv.ExtraFees > 0) lines.Add(PostingLine.CreditKey(PostingKeys.OtherRevenue, inv.ExtraFees));
        if (inv.Tax > 0) lines.Add(PostingLine.CreditKey(PostingKeys.SalesTaxOutput, inv.Tax));
        return new PostingRequest(inv.InvoiceDate, $"{(En ? "Invoice" : "فاتورة")} {inv.Number}", AccountingCodes.ModuleTrn, SourceType, inv.Id, false, lines);
    }

    // ============================================================ reads

    internal sealed record Row(Invoice I, Guid CustomerPid, string CustomerName, string? ApprovedByName, Guid? EntryPid, string? EntryNumber, bool Pending, string? EStatus, int Lines);

    internal IQueryable<Row> Rows(IQueryable<Invoice> q) => q.Select(i => new Row(i, i.Customer.PublicId, i.Customer.Name,
        i.ApprovedByUser != null ? i.ApprovedByUser.FullName : null,
        i.JournalEntry != null ? i.JournalEntry.PublicId : null, i.JournalEntry != null ? i.JournalEntry.Number : null,
        db.Set<PendingPosting>().Any(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == SourceType && p.SourceId == i.Id && p.ResolvedAt == null),
        db.Set<EInvoiceSubmission>().Where(s => s.InvoiceId == i.Id).OrderByDescending(s => s.Id).Select(s => s.Status).FirstOrDefault(),
        db.Set<InvoiceLine>().Count(l => l.InvoiceId == i.Id)));

    private static readonly IReadOnlyDictionary<string, Expression<Func<Invoice, object?>>> SortFields = new Dictionary<string, Expression<Func<Invoice, object?>>>(StringComparer.Ordinal)
    {
        ["invoiceDate"] = i => i.InvoiceDate,
        ["number"] = i => i.Number,
        ["customer"] = i => i.Customer.Name,
        ["total"] = i => i.Total,
        ["remaining"] = i => i.RemainingAmount,
        ["dueDate"] = i => i.DueDate,
        ["createdAt"] = i => i.CreatedAt,
    };

    public async Task<InvoicePage> ListAsync(InvoiceListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var query = Filter(q, today);
        var totals = await query.GroupBy(_ => 1).Select(g => new InvoiceListTotals(g.Count(), g.Sum(i => i.Total), g.Sum(i => i.PaidAmount), g.Sum(i => i.ReturnedAmount), g.Sum(i => i.RemainingAmount)))
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? new InvoiceListTotals(0, 0, 0, 0, 0);
        var paging = new PageRequest(q.Page, q.PageSize);
        var sorted = MasterDataSupport.Sort(query, q.Sort, SortFields, "-invoiceDate").ThenByDescending(i => i.Id);
        var rows = await Rows(sorted.Skip(paging.Skip).Take(paging.SafePageSize)).ToListAsync(ct).ConfigureAwait(false);
        return new InvoicePage(new PagedResult<InvoiceResponse>(rows.Select(r => ToResponse(r, today)).ToList(), paging.SafePage, paging.SafePageSize, totals.Invoices), totals);
    }

    /// <summary>The filtered invoice list as a report table for Excel/CSV/PDF export (FR-047, FR-165), with the totals row.</summary>
    public async Task<ReportTable> ExportTableAsync(InvoiceListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var query = Filter(q, today);
        var rows = await Rows(MasterDataSupport.Sort(query, q.Sort, SortFields, "-invoiceDate").ThenByDescending(i => i.Id).Take(SheetData.MaxRows)).ToListAsync(ct).ConfigureAwait(false);
        var data = rows.Select(r => ToResponse(r, today)).Select(i => (IReadOnlyList<object?>)[i.Number ?? (En ? "Draft" : "مسودة"), i.InvoiceDate, i.Customer.Name,
            i.LineCount, i.Total, i.PaidAmount + i.ReturnedAmount, i.RemainingAmount, i.DueDate, i.PaymentState, i.EInvoiceStatus]).ToList();
        var active = rows.Where(r => r.I.Status != InvoiceCodes.Void && r.I.Status != InvoiceCodes.Draft).ToList();
        IReadOnlyList<object?> totals = [En ? "Total" : "المجموع", null, null, null, active.Sum(r => r.I.Total), active.Sum(r => r.I.PaidAmount + r.I.ReturnedAmount),
            active.Sum(r => r.I.RemainingAmount), null, null, null];
        ReportColumn[] columns = En
            ? [new("Number"), new("Date", ReportValue.Date), new("Customer"), new("Lines", ReportValue.Number), new("Total", ReportValue.Money), new("Paid and returned", ReportValue.Money),
               new("Remaining", ReportValue.Money), new("Due", ReportValue.Date), new("Status"), new("E-invoicing")]
            : [new("الرقم"), new("التاريخ", ReportValue.Date), new("العميل"), new("البنود", ReportValue.Number), new("الإجمالي", ReportValue.Money), new("المدفوع والمرتجع", ReportValue.Money),
               new("المتبقي", ReportValue.Money), new("الاستحقاق", ReportValue.Date), new("الحالة"), new("الفوترة الإلكترونية")];
        var filters = new List<(string, string)>();
        if (q.From is DateOnly f) filters.Add((En ? "From" : "من", f.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        if (q.To is DateOnly t) filters.Add((En ? "To" : "إلى", t.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        if (!string.IsNullOrWhiteSpace(q.Status)) filters.Add((En ? "Status" : "الحالة", q.Status));
        return new ReportTable(En ? "Invoices" : "الفواتير", filters, columns, data, totals);
    }

    private IQueryable<Invoice> Filter(InvoiceListQuery q, DateOnly today)
    {
        var query = db.Set<Invoice>().AsNoTracking();
        query = (q.Status?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "draft" => query.Where(i => i.Status == InvoiceCodes.Draft),
            "approved" => query.Where(i => i.Status == InvoiceCodes.Approved),
            "closed" => query.Where(i => i.Status == InvoiceCodes.Closed),
            "void" => query.Where(i => i.Status == InvoiceCodes.Void),
            "active" => query.Where(i => i.Status == InvoiceCodes.Approved || i.Status == InvoiceCodes.Closed),
            _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, new FieldRef("status")),
        };
        if (Pid<Customer>(q.Customer, "customer") is Guid c) query = query.Where(i => i.Customer.PublicId == c);
        if (q.From is DateOnly from) query = query.Where(i => i.InvoiceDate >= from);
        if (q.To is DateOnly to) query = query.Where(i => i.InvoiceDate <= to);
        query = (q.Paid?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "paid" => query.Where(i => i.Status == InvoiceCodes.Closed),
            "unpaid" => query.Where(i => i.Status == InvoiceCodes.Approved && i.PaidAmount == 0 && i.ReturnedAmount == 0),
            "partial" => query.Where(i => i.Status == InvoiceCodes.Approved && (i.PaidAmount > 0 || i.ReturnedAmount > 0)),
            _ => throw Fail.Field("paid", ErrorCodes.ValidationFormat, new FieldRef("paid")),
        };
        if (q.Overdue == true) query = query.Where(i => i.Status == InvoiceCodes.Approved && i.RemainingAmount > 0 && i.DueDate < today);
        if (!string.IsNullOrWhiteSpace(q.EInvoice))
        {
            var e = q.EInvoice.Trim();
            query = e.Equals("NotRequired", StringComparison.OrdinalIgnoreCase)
                ? query.Where(i => !db.Set<EInvoiceSubmission>().Any(s => s.InvoiceId == i.Id))
                : query.Where(i => db.Set<EInvoiceSubmission>().Where(s => s.InvoiceId == i.Id).OrderByDescending(s => s.Id).Select(s => s.Status).FirstOrDefault() == e);
        }
        if (MasterDataSupport.SearchText(q.Q) is { } text) query = query.Where(i => i.Number != null && i.Number.Contains(text));
        return query;
    }

    public async Task<InvoiceDetails> GetAsync(string token, CancellationToken ct)
    {
        var inv = await support.FindAsync<Invoice>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(inv.Id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var inv = await support.FindAsync<Invoice>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, inv.Id, ct).ConfigureAwait(false);
    }

    internal async Task<InvoiceDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var row = await Rows(db.Set<Invoice>().AsNoTracking().Where(i => i.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var credits = (await db.Set<CreditNote>().AsNoTracking().Where(c => c.InvoiceId == id).OrderBy(c => c.Id)
                .Select(c => new { c.PublicId, c.Number, c.CreditDate, c.Total, c.Status }).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => new CreditNoteSummary(support.Token<CreditNote>(c.PublicId), c.Number, c.CreditDate, c.Total, c.Status)).ToList();
        var words = En ? AmountInWords.English(row.I.Total, company.CurrencyCode, company.Decimals) : AmountInWords.Arabic(row.I.Total, company.CurrencyCode, company.Decimals);
        return new InvoiceDetails(ToResponse(row, today), await LinesAsync(id, ct).ConfigureAwait(false), credits, words,
            await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false));
    }

    internal async Task<IReadOnlyList<InvoiceLineResponse>> LinesAsync(long invoiceId, CancellationToken ct)
    {
        var lines = await db.Set<InvoiceLine>().AsNoTracking().Where(l => l.InvoiceId == invoiceId).OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
            .Select(l => new
            {
                l.PublicId, l.SortOrder, l.Description, l.Quantity, l.UnitPrice, l.Amount,
                Returned = db.Set<CreditNoteLine>().Where(c => c.InvoiceLineId == l.Id && c.CreditNote.Status == InvoiceCodes.Posted).Sum(c => (decimal?)c.Quantity) ?? 0m,
                Trip = l.Trip == null ? null : new
                {
                    l.Trip.PublicId, l.Trip.ReceiptNo, l.Trip.LoadingDate, l.Trip.UnloadingDate, l.Trip.TruckPlateAtSave, Driver = l.Trip.Driver.FullName,
                    Loading = l.Trip.LoadingSite.Name, Unloading = l.Trip.UnloadingSite.Name, Material = l.Trip.Material.Name, l.Trip.LoadedWeight, l.Trip.UnloadedWeight,
                    l.Trip.CustomValues,
                },
            }).ToListAsync(ct).ConfigureAwait(false);
        return lines.Select(l => new InvoiceLineResponse(support.Token<InvoiceLine>(l.PublicId), l.SortOrder,
            l.Trip is null ? null : new Ref(support.Token<Trip>(l.Trip.PublicId), l.Trip.ReceiptNo), l.Description, l.Quantity, l.UnitPrice, l.Amount, l.Returned,
            l.Trip?.LoadingDate, l.Trip?.UnloadingDate, l.Trip?.TruckPlateAtSave, l.Trip?.Driver, l.Trip?.Loading, l.Trip?.Unloading, l.Trip?.Material,
            l.Trip?.LoadedWeight, l.Trip?.UnloadedWeight,
            CustomFieldService.Read(l.Trip?.CustomValues).ToDictionary(kv => kv.Key, kv => CustomFieldService.Text(kv.Value) ?? string.Empty, StringComparer.Ordinal))).ToList();
    }

    internal InvoiceResponse ToResponse(Row r, DateOnly today)
    {
        var i = r.I;
        var state = i.Status switch
        {
            InvoiceCodes.Draft => "Draft",
            InvoiceCodes.Void => "Void",
            InvoiceCodes.Closed => "Closed",
            _ when i.PaidAmount > 0 => "PartiallyPaid",
            _ when i.ReturnedAmount > 0 => "PartiallyReturned",
            _ => "Unpaid",
        };
        return new InvoiceResponse(support.Token<Invoice>(i.PublicId), i.Number, i.InvoiceType, new Ref(support.Token<Customer>(r.CustomerPid), r.CustomerName),
            i.InvoiceDate, i.DueDate, i.Subtotal, i.Discount, i.ExtraFees, i.ExtraFeesDescription, i.BaseAmount, i.TaxRate, i.Tax, i.Total, i.PaidAmount, i.ReturnedAmount,
            i.RemainingAmount, i.Status, state, i.Status == InvoiceCodes.Approved && i.RemainingAmount > 0 && i.DueDate < today, i.LayoutType, i.Notes,
            r.ApprovedByName, i.ApprovedAt is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
            r.EntryPid is Guid e ? new DocRef(support.Token<JournalEntry>(e), r.EntryNumber) : null, r.Pending, i.VoidReason, r.EStatus ?? "NotRequired", r.Lines,
            DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(i.RowVersion));
    }
}
