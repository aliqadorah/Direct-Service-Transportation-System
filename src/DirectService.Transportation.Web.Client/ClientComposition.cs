using System.Reflection;
using DirectService.Shared.UI.Help;
using DirectService.Shared.UI.Layout;
using DirectService.Shared.UI.Localization;
using DirectService.Transportation.Web.Client.Help;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Transportation.Web.Client.Localization;

namespace DirectService.Transportation.Web.Client;

/// <summary>
/// The browser app's static composition (Stage 2 session 15, DT-369): gives the shared shell (Shared.UI) this application's menu, record
/// links, help pages and the text of the other UI libraries, and names the libraries whose pages the router finds. Run once at start-up by
/// the browser app, the host (its sign-in pages render the shared text) and the UI tests; running it again sets the same values. Stage 4
/// replaces it with each module's contributors (INavContributor, IHelpTopicProvider, ILinkResolver).
/// </summary>
public static class ClientComposition
{
    /// <summary>The UI libraries with pages, besides the browser app itself (router and host AdditionalAssemblies): same routes as before.</summary>
    public static readonly IReadOnlyList<Assembly> PageAssemblies = [typeof(NavModel).Assembly];

    public static void Apply()
    {
        UiText.Use(AppTexts.Billing(), AppTexts.Accounting(), AppTexts.Budgets(), PlatformTexts.Platform());
        NavModel.Use(AppMenu.Company, AppMenu.Console);
        Links.Use(new AppLinks());
        HelpTopics.Use(AppHelpTopics.All, AppHelpTopics.ConsoleTopics);
    }
}
