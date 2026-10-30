using DirectService.Transportation.Web.Client.Localization;
using DirectService.Platform.UI.Localization;
using DirectService.Shared.UI.Localization;
using DirectService.Transportation.Web.Client.Services;
using DirectService.Shared.UI.Services;
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

    /// <summary>
    /// Finds an element and fires its event in one step on the renderer's dispatcher (DT-243): a render started by earlier asynchronous
    /// work (an API answer, a debounced search) runs on that dispatcher too, so it can no longer come between finding the element and
    /// firing — which replaced the element's event handler ("There is no event handler with ID …") on the slower CI runner. Find inside
    /// the action, never before it. Returns once the event's synchronous part (and its render) is done, as the plain bUnit calls do.
    /// </summary>
    protected void Act(Action findAndFire) => Renderer.Dispatcher.InvokeAsync(findAndFire).GetAwaiter().GetResult();
}
