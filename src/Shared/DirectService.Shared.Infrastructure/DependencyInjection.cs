using DirectService.Shared.Infrastructure.Collaboration;
using DirectService.Shared.Infrastructure.Configuration;
using DirectService.Shared.Infrastructure.Files;
using DirectService.Shared.Infrastructure.Imports;
using DirectService.Shared.Infrastructure.Persistence;
using DirectService.Shared.Infrastructure.Persistence.Interceptors;
using DirectService.Shared.Infrastructure.Printing;
using DirectService.Shared.Infrastructure.Reports;
using DirectService.Shared.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DirectService.Shared.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The shared building blocks every module uses: the Shared part of the model (cfg tables), the save interceptors (audit, live
    /// changes), URL tokens, numbering, the entity resolver, PDF and QR rendering, the document printer (SharedKernel), the report exporter, the file store, spreadsheets, column views, the global
    /// search (hits from the modules' <see cref="ISearchProvider"/>s) and record locks (the holder's name through a module's
    /// <see cref="IUserNames"/>). The host registers the DbContext.
    /// </summary>
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IModelContributor, SharedModelContributor>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddScoped<LiveChangeInterceptor>();
        services.AddScoped<IAfterCommit>(sp => sp.GetRequiredService<LiveChangeInterceptor>());

        services.AddOptions<UrlTokenOptions>().Bind(configuration.GetSection(UrlTokenOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<UrlTokenOptions>, UrlTokenOptionsValidator>();
        services.AddSingleton<IUrlTokenService, UrlTokenService>();
        services.AddScoped<INumberingService, NumberingService>();
        services.AddScoped<IEntityResolver, EntityResolver>();
        services.AddSingleton<IPdfRenderer, ChromiumPdfRenderer>();
        services.AddSingleton<IQrCodeRenderer, QrCodeRenderer>();
        services.AddSingleton<IFileStore, LocalFileStore>();
        services.AddSingleton<ISpreadsheetService, SpreadsheetService>();
        services.AddScoped<ColumnViewService>();
        services.AddScoped<FieldCatalogs>();
        services.AddScoped<CustomFieldService>();
        services.AddScoped<ICustomFields>(sp => sp.GetRequiredService<CustomFieldService>());
        services.AddScoped<StandardFieldService>();
        services.AddScoped<IStandardFields>(sp => sp.GetRequiredService<StandardFieldService>());
        services.AddScoped<GlobalSearchService>();
        services.AddScoped<ImportTemplateService>();
        services.AddScoped<RecordLockService>();
        services.AddScoped<IRecordLockGuard>(sp => sp.GetRequiredService<RecordLockService>());
        services.AddScoped<PrintMode>();
        services.AddScoped<DocumentPrinter>();
        services.AddScoped<IssuedNames>();
        services.AddScoped<IReportExporter, ReportExporter>();
        return services;
    }
}
