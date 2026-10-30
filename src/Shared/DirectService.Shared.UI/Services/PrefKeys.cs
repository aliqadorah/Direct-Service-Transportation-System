namespace DirectService.Shared.UI.Services;

/// <summary>
/// Device preferences in the browser's localStorage (platform design §e, E-28). <c>wwwroot/js/ui.js</c> stores each under <c>"dst." + key</c>
/// and <c>wwwroot/js/theme.js</c> reads <c>"dst.theme"</c> before sign-in; a key ends with the user's URL token (stable through the pinned
/// token labels, E-3). Pinned literals: renaming one forgets every user's saved choice.
/// </summary>
public static class PrefKeys
{
    /// <summary>The prefix ui.js adds to every key.</summary>
    public const string StoragePrefix = "dst.";

    /// <summary>Theme of a signed-in user (+ user token, DT-244).</summary>
    public const string Theme = "theme.";

    /// <summary>Side menu state of a user (+ user token).</summary>
    public const string Nav = "nav.";
}
