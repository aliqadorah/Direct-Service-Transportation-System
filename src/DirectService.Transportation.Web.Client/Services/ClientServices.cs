using DirectService.Transportation.Web.Client.Localization;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Web.Client.Services;

public static class ClientServices
{
    /// <summary>Services of the design system and shell (one instance per browser tab).</summary>
    public static IServiceCollection AddClientUi(this IServiceCollection services)
    {
        services.AddScoped<Loc>();
        services.AddScoped<ShellState>();
        services.AddScoped<ToastService>();
        services.AddScoped<UiJs>();
        return services;
    }
}
