using System.Text.Json;
using System.Text.RegularExpressions;

namespace DirectService.Platform.Contracts.Collaboration;

/// <summary>
/// How a notification's text is built from its words and stored parameters (DT-189), for the platform's types and every module's: the text
/// is built in the reader's language when it is shown, so a notification reads correctly in Arabic and English whenever it was created.
/// Notifications stored before (JSON {"Ar","En"} or plain text) are read back into parameters where their wording is known (each type's
/// own reader of the older English text), otherwise shown as stored. Moved from <c>NotificationTexts</c> (Stage 2 session 14).
/// </summary>
public static partial class NotificationWording
{
    private sealed record Legacy(string? Ar, string? En);

    private static readonly JsonSerializerOptions LegacyJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The parameters of a stored message: the current form, or read back from an older stored text by <paramref name="fromOldEnglish"/>; null when unknown.</summary>
    public static Dictionary<string, string>? Parameters(string message, Func<string, Dictionary<string, string>?> fromOldEnglish)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(fromOldEnglish);
        if (message.StartsWith("{\"p\"", StringComparison.Ordinal)) return PlatformNotifications.Read(message);
        var (_, en) = LegacyText(message);
        return en is null ? null : fromOldEnglish(en);
    }

    /// <summary>The text of a notification in <paramref name="language"/>; extra parameters (e.g. a count computed when shown) are added to the stored ones.</summary>
    public static NotificationText Render(NotificationWords? words, Dictionary<string, string>? parameters, string message, string language,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var ar = language != "en";
        var p = parameters;
        if (p is not null && words is { } w)
        {
            if (extra is not null) foreach (var (k, v) in extra) p[k] = v;
            var details = w.DetailsParam is { } d && p.TryGetValue(d, out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw : null;
            return new NotificationText(ar ? w.TitleAr : w.TitleEn, Fill(ar ? w.MessageAr : w.MessageEn, p), details);
        }
        // Unknown wording: the stored text in the reader's language (older bilingual JSON), or as stored.
        var (storedAr, storedEn) = LegacyText(message);
        var text = (ar ? storedAr : storedEn) ?? storedAr ?? storedEn ?? message;
        var title = words is { } known ? (ar ? known.TitleAr : known.TitleEn) : (ar ? "إشعار" : "Notification");
        return new NotificationText(title, text, null);
    }

    private static string Fill(string template, Dictionary<string, string> p) =>
        Placeholder().Replace(template, m => p.TryGetValue(m.Groups[1].Value, out var v) && v.Length > 0 ? v : "—");

    /// <summary>Arabic and English of an older stored message: JSON {"Ar","En"}, or the plain "… / …" text of the first notifications.</summary>
    private static (string? Ar, string? En) LegacyText(string message)
    {
        if (message.StartsWith('{'))
        {
            try
            {
                var t = JsonSerializer.Deserialize<Legacy>(message, LegacyJson);
                return (t?.Ar, t?.En);
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }
        var slash = message.IndexOf(" / ", StringComparison.Ordinal);
        return slash > 0 ? (message, message) : (null, null);
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
