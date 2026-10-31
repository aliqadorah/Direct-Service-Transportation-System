using DirectService.Accounting.Application.Posting;
using DirectService.Accounting.Contracts.Posting;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Accounting.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the accounting services: the posting engine and the posting contract over it (<see cref="IAccountingPosting"/>), Accounting's
    /// posting keys (Stage 3 session 4a). The host calls it after the Transportation application, so the posting-key providers keep their order
    /// (TRN, then ACC). The period guard and the key catalogue (<see cref="IPeriodGuard"/>, <see cref="IPostingKeyCatalog"/>) are still given by
    /// the transport module until the period and key services move (sessions 6 and Stage 5).
    /// </summary>
    public static IServiceCollection AddAccountingApplication(this IServiceCollection services)
    {
        services.AddScoped<PostingEngine>();
        services.AddScoped<IAccountingPosting, AccountingPostingAdapter>();
        services.AddSingleton<IPostingKeyProvider, AccountingPostingKeyProvider>();
        return services;
    }
}
