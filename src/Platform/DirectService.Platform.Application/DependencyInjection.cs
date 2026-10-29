using DirectService.Platform.Application.Companies;
using DirectService.Platform.Application.Errors;
using DirectService.Platform.Application.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Platform.Application;

public static class DependencyInjection
{
    /// <summary>
    /// The Platform services (platform design a.5). The host calls it next to the modules' registrations; a module adds its messages through
    /// <see cref="IErrorTextProvider"/>.
    /// </summary>
    public static IServiceCollection AddPlatformApplication(this IServiceCollection services)
    {
        services.AddScoped<IUserNames, UserNames>();
        services.AddScoped<ICompanyInfo, CompanyInfo>();
        services.AddSingleton<IErrorText, ErrorText>();
        return services;
    }
}
