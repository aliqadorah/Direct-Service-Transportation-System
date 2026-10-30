using DirectService.Shared.UI.Localization;
using DirectService.Shared.UI.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Shared.UI.Services;

public static class ClientServices
{
    /// <summary>Services of the design system and shell (one instance per browser tab).</summary>
    public static IServiceCollection AddClientUi(this IServiceCollection services)
    {
        services.AddScoped<Loc>();
        services.AddScoped<ShellState>();
        services.AddScoped<ToastService>();
        services.AddScoped<UiJs>();
        services.AddScoped<LiveUpdates>();
        return services;
    }
}
