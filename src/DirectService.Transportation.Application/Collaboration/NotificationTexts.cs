using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>A notification as the reader sees it: a short title, one readable sentence, and the technical details (codes, raw answers) kept apart.</summary>
public sealed record NotificationText(string Title, string Message, string? Details);

/// <summary>
/// The words of every notification type (DT-189). A notification stores its type and parameters (JSON {"p":{…}}); the text is built in the
/// reader's language when it is shown, so a notification reads correctly in Arabic and English whenever it was created. Notifications stored
/// before (JSON {"Ar","En"} or plain text) are read back into parameters where their wording is known, otherwise shown as stored.
/// The screen isolates left-to-right runs (numbers, codes such as INV-00007) so they keep their order inside Arabic sentences.
/// </summary>
public static partial class NotificationTexts
{
    private sealed record Words(string TitleAr, string TitleEn, string MessageAr, string MessageEn, string? DetailsParam = null);

    private static readonly Dictionary<string, Words> Catalog = new(StringComparer.Ordinal)
    {
        [NotificationService.EInvoiceRejected] = new("رُفضت فاتورة إلكترونية", "E-invoice rejected",
            "رفض نظام الفوترة الوطني المستند {number}. افتحه لتصحيحه وإعادة إرساله.",
            "The national e-invoicing system refused {number}. Open it to correct and resubmit.", "reason"),
        [NotificationService.EInvoiceSetupError] = new("الفوترة الإلكترونية غير مضبوطة بشكل صحيح", "E-invoicing is not set up correctly",
            "رفض نظام جو فوترة بيانات الاعتماد. افتح إعدادات الفوترة الإلكترونية. الفواتير المتأثرة: {count}",
            "JoFotara refused the credentials. Open e-invoicing settings. Affected invoices: {count}", "reason"),
        [NotificationService.PostingFailed] = new("تعذّر ترحيل قيد", "An entry was not posted",
            "ينتظر القيد في الترحيلات المعلّقة: {description}", "The entry waits in pending postings: {description}"),
        [NotificationService.ExportReady] = new("التصدير جاهز", "Export ready", "الملف {file} جاهز للتنزيل.", "{file} is ready to download."),
        [NotificationService.ExportFailed] = new("تعذّر التصدير", "Export failed",
            "تعذّر إعداد التصدير. أعد المحاولة أو ضيّق الفترة.", "The export could not be prepared. Try again or narrow the period."),
        [NotificationService.InvitationAccepted] = new("قُبلت الدعوة", "Invitation accepted",
            "قبل {name} الدعوة وانضم إلى الشركة.", "{name} accepted the invitation and joined the company."),
        [NotificationService.SupportRequested] = new("طلب وصول للدعم الفني", "Support access requested",
            "يطلب الدعم الفني لدى مزوّد الخدمة الوصول إلى بياناتكم: {reason}", "Provider support requests access to your data: {reason}"),
        [NotificationService.PaymentFailed] = new("تعذّر تحصيل التجديد", "Renewal payment failed",
            "تعذّر تحصيل قيمة التجديد؛ لديكم {days} أيام لتحديث وسيلة الدفع.", "The renewal payment failed; you have {days} days to update the payment method."),
    };

    /// <summary>Every type with words (the tests check that each one renders in both languages).</summary>
    public static IReadOnlyCollection<string> Types => Catalog.Keys;


    private sealed record Stored([property: System.Text.Json.Serialization.JsonPropertyName("p")] Dictionary<string, string>? P);

    private sealed record Legacy(string? Ar, string? En);

    private static readonly JsonSerializerOptions LegacyJson = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The stored form of the parameters, trimmed so it fits the 500-character column (longest values shortened first).</summary>
    public static string Store(IReadOnlyDictionary<string, string?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var p = parameters.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(new Stored(p), Json);
        while (json.Length > NotificationService.MaxMessage)
        {
            var longest = p.MaxBy(kv => kv.Value.Length);
            p[longest.Key] = Cut(longest.Value, Math.Max(1, longest.Value.Length - Math.Max(2, json.Length - NotificationService.MaxMessage)));
            json = JsonSerializer.Serialize(new Stored(p), Json);
        }
        return json;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : max <= 1 ? "…" : text[..(max - 1)] + "…";

    /// <summary>The parameters of a stored message: the current form, or read back from an older stored text; null when unknown.</summary>
    public static Dictionary<string, string>? Parameters(string type, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.StartsWith("{\"p\"", StringComparison.Ordinal))
        {
            try
            {
                return JsonSerializer.Deserialize<Stored>(message)?.P ?? [];
            }
            catch (JsonException)
            {
                return null;
            }
        }
        var (_, en) = LegacyText(message);
        if (en is null) return null;
        Match m;
        switch (type)
        {
            case NotificationService.EInvoiceRejected when (m = OldRejected().Match(en)).Success:
                return new(StringComparer.Ordinal) { ["number"] = m.Groups["number"].Value.Trim(), ["reason"] = m.Groups["reason"].Value.Trim() };
            case NotificationService.PostingFailed when (m = OldPosting().Match(en)).Success:
                return new(StringComparer.Ordinal) { ["description"] = m.Groups["v"].Value.Trim() };
            case NotificationService.ExportReady when (m = OldExport().Match(en)).Success:
                return new(StringComparer.Ordinal) { ["file"] = m.Groups["v"].Value.Trim() };
            case NotificationService.SupportRequested when (m = OldSupport().Match(en)).Success:
                return new(StringComparer.Ordinal) { ["reason"] = m.Groups["v"].Value.Trim() };
            case NotificationService.InvitationAccepted when (m = OldInvitation().Match(en)).Success:
                return new(StringComparer.Ordinal) { ["name"] = m.Groups["v"].Value.Trim() };
            case NotificationService.PaymentFailed:
                return new(StringComparer.Ordinal) { ["days"] = (OldDays().Match(en) is { Success: true } d ? d.Value : "7") };
            case NotificationService.ExportFailed:
                return [];
            default:
                return null;
        }
    }

    /// <summary>The text of a notification in <paramref name="language"/>; extra parameters (e.g. a count computed when shown) are added to the stored ones.</summary>
    public static NotificationText Render(string type, string message, string language, IReadOnlyDictionary<string, string>? extra = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var ar = language != "en";
        var p = Parameters(type, message);
        if (p is not null && Catalog.TryGetValue(type, out var w))
        {
            if (extra is not null) foreach (var (k, v) in extra) p[k] = v;
            var details = w.DetailsParam is { } d && p.TryGetValue(d, out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw : null;
            return new NotificationText(ar ? w.TitleAr : w.TitleEn, Fill(ar ? w.MessageAr : w.MessageEn, p), details);
        }
        // Unknown wording: the stored text in the reader's language (older bilingual JSON), or as stored.
        var (storedAr, storedEn) = LegacyText(message);
        var text = (ar ? storedAr : storedEn) ?? storedAr ?? storedEn ?? message;
        var title = Catalog.TryGetValue(type, out var known) ? (ar ? known.TitleAr : known.TitleEn) : (ar ? "إشعار" : "Notification");
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

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^An e-invoice was rejected:\s*(?<number>\S+)\s*—\s*(?<reason>.*)$", RegexOptions.Singleline)]
    private static partial Regex OldRejected();

    [GeneratedRegex(@"^An entry could not be posted and waits in pending postings:\s*(?<v>.*)$", RegexOptions.Singleline)]
    private static partial Regex OldPosting();

    [GeneratedRegex(@"^Export ready:\s*(?<v>.*)$", RegexOptions.Singleline)]
    private static partial Regex OldExport();

    [GeneratedRegex(@"^Provider support requests access to your data:\s*(?<v>.*)$", RegexOptions.Singleline)]
    private static partial Regex OldSupport();

    [GeneratedRegex(@"^(?<v>.+?) قبل الدعوة / accepted the invitation$", RegexOptions.Singleline)]
    private static partial Regex OldInvitation();

    [GeneratedRegex(@"\d+")]
    private static partial Regex OldDays();
}
