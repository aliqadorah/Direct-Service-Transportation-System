using DirectService.Transportation.Application.ApAr;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Cash;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Application.Invoicing;
using DirectService.Transportation.Application.Ledger;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Application.Trips;
using DirectService.Platform.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;
using DirectService.Platform.Application.Staff;
using DirectService.Platform.Application.Email;
using DirectService.Platform.Application.Tenants;

namespace DirectService.Transportation.Application;

public static class DependencyInjection
{
    /// <summary>Registers the application layer (use-case services and request-scoped context).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<DataScope>();
        services.AddScoped<IDataScope>(sp => sp.GetRequiredService<DataScope>());
        services.AddScoped<IChangeContext, ChangeContext>();
        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());

        services.AddScoped<CompanyProvisioningService>();
        services.AddScoped<ICompanyProvisioner>(sp => sp.GetRequiredService<CompanyProvisioningService>());
        // What the module gives the Platform services (platform design a.3, DT-346).
        services.AddScoped<IUsageMeter, Modules.TripUsageMeter>();
        services.AddScoped<ITenantDataEraser, Modules.TransportationDataEraser>();
        services.AddSingleton<IPermissionCatalog, Modules.TransportationPermissions>();
        services.AddScoped<INotificationTypeProvider, TransportationNotificationTypes>();   // words, links, set-up count (Stage 2 session 14)
        services.AddScoped<OperationSettingsService>();   // the operations section of the company settings (Stage 2 session 14)
        services.AddSingleton<IErrorTextProvider, TransportationErrorTexts>();
        services.AddScoped<IPeriodGuard, PeriodGuard>();

        // Accounting core and posting engine (B2)
        services.AddScoped<PostingEngine>();
        services.AddScoped<PostingSources>();
        services.AddScoped<PendingPostingService>();
        services.AddScoped<PartyResolver>();
        services.AddScoped<ChartOfAccountsService>();
        services.AddScoped<OpeningBalanceService>();
        services.AddScoped<PostingKeyService>();
        services.AddScoped<CashBankService>();
        services.AddScoped<PeriodService>();
        services.AddScoped<JournalService>();
        services.AddScoped<JournalDraftService>();
        services.AddScoped<JournalTemplateService>();
        services.AddScoped<TaxCodeService>();
        services.AddScoped<AccountingDashboardService>();
        services.AddScoped<Budgets.BudgetService>();
        services.AddScoped<Budgets.BudgetImportService>();
        services.AddScoped<Budgets.BudgetReportService>();
        services.AddScoped<JournalExportService>();
        services.AddScoped<ChartImportService>();
        services.AddScoped<JournalImportService>();

        // Master data and field configuration (B3)
        services.AddScoped<IFieldCatalogProvider, TransportationFieldCatalog>();
        services.AddScoped<MasterDataSupport>();
        services.AddScoped<IAuditVocabularyProvider, Audit.TransportationAuditVocabulary>();
        services.AddScoped<DriverService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<PartnerService>();
        services.AddScoped<MaterialService>();
        services.AddScoped<LoadingSiteService>();
        services.AddScoped<SiteService<Domain.Trn.LoadingSite>>(sp => sp.GetRequiredService<LoadingSiteService>());
        services.AddScoped<UnloadingSiteService>();
        services.AddScoped<SiteService<Domain.Trn.UnloadingSite>>(sp => sp.GetRequiredService<UnloadingSiteService>());
        services.AddScoped<OwnershipTypeService>();
        services.AddScoped<MasterExchangeService>();

        // Pricing (B4)
        services.AddScoped<RateHistoryLog>();
        services.AddScoped<TariffService>();
        services.AddScoped<MonthlyRateService>();
        services.AddScoped<PricingResolver>();
        services.AddScoped<PricingExchangeService>();

        // Trips (B5)
        services.AddScoped<TripService>();
        services.AddScoped<TripImportService>();
        services.AddScoped<IImportTemplateProvider, Imports.TransportationImportTemplates>();
        services.AddScoped<TripExportService>();
        services.AddScoped<TripPostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<TripPostingHandler>());

        // Invoicing and credit notes (B6)
        services.AddScoped<InvoiceService>();
        services.AddScoped<CreditNoteService>();
        services.AddScoped<InvoiceLayoutService>();
        services.AddScoped<InvoicePrintService>();
        services.AddScoped<InvoicePostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<InvoicePostingHandler>());
        services.AddScoped<CreditNotePostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<CreditNotePostingHandler>());

        // E-invoicing (B7)
        services.AddScoped<EInvoiceSettingsService>();
        services.AddScoped<EInvoiceDocumentBuilder>();
        services.AddScoped<EInvoiceService>();

        // Cash, drivers and partners (B8)
        services.AddScoped<CashSupport>();
        services.AddScoped<PreviousBalances>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<AdvanceService>();
        services.AddScoped<SettlementService>();
        services.AddScoped<PartnerPaymentService>();
        services.AddScoped<AccountStatementService>();
        services.AddScoped<VoucherPrintService>();
        services.AddScoped<ReceiptPostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<ReceiptPostingHandler>());
        services.AddScoped<AdvancePostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<AdvancePostingHandler>());
        services.AddScoped<SettlementPostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<SettlementPostingHandler>());
        services.AddScoped<PartnerPaymentPostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<PartnerPaymentPostingHandler>());

        // AP/AR, financial reports and maintenance (B9)
        services.AddScoped<ApArPartyService>();
        services.AddScoped<ApArDocumentService>();
        services.AddScoped<ApArPaymentService>();
        services.AddScoped<ApArReturnService>();
        services.AddScoped<ApArPrintService>();
        services.AddScoped<AgeingService>();
        services.AddScoped<LedgerReportService>();
        services.AddScoped<LedgerReportExports>();
        services.AddScoped<MaintenanceService>();

        // Reports and dashboard (B10)
        services.AddScoped<StandardReportService>();
        services.AddScoped<IReportSourceProvider, TransportationReportSources>();
        services.AddSingleton(new SavedReportOptions { AdministratorPermission = Contracts.Security.PermissionKeys.SettingsManage });
        services.AddScoped<DashboardService>();

        // Platform (B12)
        services.AddScoped<IDemoDataContributor, Modules.TransportationDemoData>();
        services.AddScoped<ISetupStepProvider, Modules.TransportationSetupSteps>();
        services.AddScoped<IHealthFigureSource, Modules.TransportationHealthFigures>();
        services.AddScoped<DriverPersonalDataService>();   // drivers' personal data (users: the platform's PersonalDataService, Stage 2 session 14)

        // Collaboration (B11)
        services.AddScoped<ISearchProvider, TransportationSearch>();
        services.AddScoped<IExportJobProvider, TransportationExportJobs>();
        services.AddSingleton(new ExportOptions { ReportPermission = Contracts.Security.PermissionKeys.ReportsView });
        services.AddScoped<SupplierBillPostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<SupplierBillPostingHandler>());
        services.AddScoped<ReceivablePostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<ReceivablePostingHandler>());
        services.AddScoped<ApArPaymentPostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<ApArPaymentPostingHandler>());
        services.AddScoped<ApArReturnPostingHandler>();
        services.AddScoped<IPostingSourceHandler>(sp => sp.GetRequiredService<ApArReturnPostingHandler>());
        return services;
    }
}
