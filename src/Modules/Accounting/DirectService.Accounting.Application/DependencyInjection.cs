using DirectService.Accounting.Contracts.Parties;
using DirectService.Accounting.Application.Parties;
using DirectService.Accounting.Application.Journal;
using DirectService.Accounting.Contracts.Modules;
using DirectService.Accounting.Application.Books;
using DirectService.Accounting.Application.Posting;
using DirectService.Accounting.Contracts.Posting;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Accounting.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the accounting services: the posting engine and the posting contract over it (<see cref="IAccountingPosting"/>), Accounting's
    /// posting keys (Stage 3 session 4a); the period guard and the period, opening-balance and account-mapping services (session 6). The host
    /// calls it after the Transportation application, so the posting-key providers keep their order (TRN, then ACC). The key catalogue
    /// (<see cref="IPostingKeyCatalog"/>, the PRD order of every product's keys) is still given by the transport module until Stage 5.
    /// </summary>
    public static IServiceCollection AddAccountingApplication(this IServiceCollection services)
    {
        services.AddScoped<PostingEngine>();
        services.AddScoped<IPostingReadiness>(sp => sp.GetRequiredService<PostingEngine>());   // setup steps and dashboard (Stage 3 session 6c)
        services.AddScoped<IAccountingPosting, AccountingPostingAdapter>();
        services.AddSingleton<IPostingKeyProvider, AccountingPostingKeyProvider>();

        // Periods, opening balances, account mapping (Stage 3 session 6).
        services.AddScoped<IPeriodGuard, PeriodGuard>();
        services.AddScoped<PeriodService>();
        services.AddScoped<OpeningBalanceService>();
        services.AddScoped<PostingKeyService>();

        // Chart of accounts with its template and import, cost centres, cash boxes and bank accounts (Stage 3 session 6c).
        services.AddScoped<ChartOfAccountsService>();
        services.AddScoped<IChartOfAccountsSetup>(sp => sp.GetRequiredService<ChartOfAccountsService>());
        services.AddScoped<ChartImportService>();
        services.AddScoped<CashBankService>();

        // Parties over the modules' providers (TRN registered by the transport module first), the journal with drafts, import and export, pending
        // postings and the source links of entries (Stage 3 session 6c).
        services.AddScoped<IPartyProvider, AccountingPartyProvider>();
        services.AddScoped<PartyResolver>();
        services.AddScoped<IPartyLookup>(sp => sp.GetRequiredService<PartyResolver>());
        services.AddScoped<PostingSources>();
        services.AddScoped<IPostingSourceLinks>(sp => sp.GetRequiredService<PostingSources>());
        services.AddScoped<PendingPostingService>();
        services.AddScoped<JournalService>();
        services.AddScoped<JournalDraftService>();
        services.AddScoped<JournalExportService>();
        services.AddScoped<JournalImportService>();

        // Tax codes and journal templates (Stage 3 session 6c).
        services.AddScoped<TaxCodeService>();
        services.AddScoped<JournalTemplateService>();
        services.AddScoped<MaintenanceService>();   // accounting maintenance (FR-135)
        return services;
    }

    /// <summary>Registers a module's handler of the posting contract (one per Module + Type; Stage 3 session 2, here since session 4b).</summary>
    public static IServiceCollection AddPostingSourceHandler<THandler>(this IServiceCollection services) where THandler : class, IPostingSourceHandler
    {
        services.AddScoped<THandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<THandler>());
        return services;
    }
}
