using DirectService.Accounting.Contracts.Modules;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Invoicing;

/// <summary>
/// The transport module's pre-close check of a period (FR-134; Stage 3 session 6): draft invoices dated in the month. The period service of
/// Accounting counts them as unapproved drafts.
/// </summary>
public sealed class DraftInvoicesCloseCheck(IAppDbContext db) : IPeriodCloseCheck
{
    public const string Code = "DraftInvoices";

    public async Task<IReadOnlyList<PeriodCloseIssue>> CheckAsync(string periodMonth, CancellationToken ct)
    {
        var first = AccountingCodes.FirstDay(periodMonth);
        var last = first.AddMonths(1).AddDays(-1);
        var drafts = await db.Set<Invoice>().AsNoTracking()
            .CountAsync(i => i.Status == "Draft" && i.InvoiceDate >= first && i.InvoiceDate <= last, ct).ConfigureAwait(false);
        return [new PeriodCloseIssue(Code, drafts)];
    }
}
