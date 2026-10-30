using System.Globalization;

namespace DirectService.Shared.UI.Localization;

/// <summary>
/// The interface language of the signed-in user (FR-178): Arabic (RTL, default) or English (LTR). Components read text through the
/// indexer; every key has both languages (<see cref="UiText"/>, test-enforced like the API error catalogue).
/// </summary>
public sealed class Loc
{
    public string Language { get; private set; } = "ar";

    public bool En => Language == "en";

    public string Dir => En ? "ltr" : "rtl";

    /// <summary>Raised after <see cref="SetLanguage"/> so the shell can re-render.</summary>
    public event Action? Changed;

    public void SetLanguage(string? code)
    {
        var lang = string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar";
        if (lang == Language) return;
        Language = lang;
        Changed?.Invoke();
    }

    public string this[string key] => UiText.All.TryGetValue(key, out var t) ? (En ? t.En : t.Ar) : key;

    /// <summary>The text of a key in both languages (searching menu pages by either name, DT-239).</summary>
    public static string Both(string key) => UiText.All.TryGetValue(key, out var t) ? t.Ar + " " + t.En : key;

    /// <summary>Text with <c>{0}</c>-style arguments (numbers are formatted with Western digits, DT-65).</summary>
    public string F(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, this[key], args);

    /// <summary>Picks between an Arabic and an English value held by the caller (names from the API, e.g. Name / NameEn).</summary>
    public string Pick(string ar, string? en) => En && !string.IsNullOrWhiteSpace(en) ? en : ar;
}
