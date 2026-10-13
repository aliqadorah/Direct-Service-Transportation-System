using DirectService.Transportation.Web.Client.Localization;
using DirectService.Transportation.Web.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>bUnit context with the design-system services; JS calls are accepted without a browser (loose mode).</summary>
public abstract class UiTestBase : BunitContext
{
    protected UiTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        UiModule = JSInterop.SetupModule("./js/ui.js");
        UiModule.Mode = JSRuntimeMode.Loose;
        Services.AddClientUi();
    }

    /// <summary>The design system's JS module (<c>js/ui.js</c>): its calls are recorded for checks.</summary>
    protected BunitJSModuleInterop UiModule { get; }

    protected Loc L => Services.GetRequiredService<Loc>();

    protected ShellState Shell => Services.GetRequiredService<ShellState>();
}
