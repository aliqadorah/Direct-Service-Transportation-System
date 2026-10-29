using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Domain.Acc;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using DirectService.Transportation.Infrastructure.EInvoicing;
using DirectService.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Web.Workers;

/// <summary>
/// Submits queued e-invoices automatically (FR-061, NFR-17): woken when a document is queued or accepted, otherwise every 2 minutes.
/// Each company is processed in its own scope as a system job; suspended tenants and archived companies are skipped. Back-off after
/// technical failures is applied by <see cref="EInvoiceService.DueAsync"/>; documents never block data entry.
/// </summary>
internal sealed partial class EInvoiceWorker(IServiceScopeFactory scopes, EInvoiceSignal signal, ILogger<EInvoiceWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly string[] ScopeFilters = [QueryFilters.Tenant, QueryFilters.Company];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // A signal arrives before the approving transaction commits: give it a moment.
            if (await signal.WaitAsync(Interval, stoppingToken).ConfigureAwait(false))
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(log, ex);
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        List<(long TenantId, long CompanyId)> companies;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
            var queued = db.Set<EInvoiceSubmission>().IgnoreQueryFilters(ScopeFilters).AsNoTracking().Where(s => s.Status == EInvoiceCodes.Queued)
                .Select(s => new { s.TenantId, s.CompanyId })
                .Concat(db.Set<ApArEInvoiceSubmission>().IgnoreQueryFilters(ScopeFilters).AsNoTracking().Where(s => s.Status == EInvoiceCodes.Queued)
                    .Select(s => new { s.TenantId, s.CompanyId }));
            companies = (await queued
                .Join(db.Set<Company>().IgnoreQueryFilters(ScopeFilters), s => s.CompanyId, c => c.Id, (s, c) => new { s, c })
                .Join(db.Set<Tenant>().IgnoreQueryFilters(ScopeFilters), x => x.c.TenantId, t => t.Id, (x, t) => new { x.s, x.c, t })
                .Where(x => x.c.Status == "Active" && (x.t.Status == "Active" || x.t.Status == "Trial"))
                .Select(x => new { x.s.TenantId, x.s.CompanyId }).Distinct().ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.TenantId, x.CompanyId)).ToList();
        }

        var accepted = 0;
        foreach (var (tenantId, companyId) in companies)
        {
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<DataScope>().Set(tenantId, companyId, null);
            var request = sp.GetRequiredService<RequestContext>();
            request.TenantId = tenantId;
            request.CompanyId = companyId;
            request.Language = "ar";
            var service = sp.GetRequiredService<EInvoiceService>();
            foreach (var id in await service.DueAsync(200, ct).ConfigureAwait(false))
            {
                try
                {
                    if (await service.ProcessAsync(id, manual: false, ct).ConfigureAwait(false)) accepted++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSubmissionFailed(log, id, ex);
                }
            }
            // Accounting receivable documents and their returns (D-15).
            foreach (var id in await service.DueApArAsync(200, ct).ConfigureAwait(false))
            {
                try
                {
                    if (await service.ProcessApArAsync(id, manual: false, ct).ConfigureAwait(false)) accepted++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSubmissionFailed(log, id, ex);
                }
            }
        }
        return accepted;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "E-invoicing run failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "E-invoice submission {SubmissionId} failed.")]
    private static partial void LogSubmissionFailed(ILogger logger, long submissionId, Exception exception);
}
