using System.Text.RegularExpressions;
using DirectService.Platform.Contracts.Collaboration;

namespace DirectService.Platform.Application.Notifications;

/// <summary>
/// The words of the platform's notification types (DT-189; the module types' words are given by their <see cref="Contracts.Modules.INotificationTypeProvider"/>
/// since Stage 2 session 14, every text unchanged). A notification stores its type and parameters (JSON {"p":{…}}); the text is built in the
/// reader's language when it is shown (<see cref="NotificationWording"/>). Notifications stored before (JSON {"Ar","En"} or plain text) are
/// read back into parameters where their wording is known, otherwise shown as stored. The screen isolates left-to-right runs (numbers, codes
/// such as INV-00007) so they keep their order inside Arabic sentences.
/// </summary>
public static partial class NotificationTexts
{
    private static readonly Dictionary<string, NotificationWords> Catalog = new(StringComparer.Ordinal)
    {
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

    /// <summary>Every platform type with words (the tests check that each one renders in both languages).</summary>
    public static IReadOnlyCollection<string> Types => Catalog.Keys;

    /// <summary>The stored form of the parameters (<see cref="PlatformNotifications.Store"/>).</summary>
    public static string Store(IReadOnlyDictionary<string, string?> parameters) => PlatformNotifications.Store(parameters);

    /// <summary>The parameters of a stored message: the current form, or read back from an older stored text; null when unknown.</summary>
    public static Dictionary<string, string>? Parameters(string type, string message) => NotificationWording.Parameters(message, en => FromOldEnglish(type, en));

    private static Dictionary<string, string>? FromOldEnglish(string type, string en)
    {
        Match m;
        switch (type)
        {
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

    /// <summary>The text of a notification in <paramref name="language"/> (a platform type, or one without words: shown as stored).</summary>
    public static NotificationText Render(string type, string message, string language, IReadOnlyDictionary<string, string>? extra = null) =>
        NotificationWording.Render(Catalog.GetValueOrDefault(type), Parameters(type, message), message, language, extra);

    public static string Number(int value) => PlatformNotifications.Number(value);

    [GeneratedRegex(@"^Export ready:\s*(?<v>.*)$", RegexOptions.Singleline)]
    private static partial Regex OldExport();

    [GeneratedRegex(@"^Provider support requests access to your data:\s*(?<v>.*)$", RegexOptions.Singleline)]
    private static partial Regex OldSupport();

    [GeneratedRegex(@"^(?<v>.+?) قبل الدعوة / accepted the invitation$", RegexOptions.Singleline)]
    private static partial Regex OldInvitation();

    [GeneratedRegex(@"\d+")]
    private static partial Regex OldDays();
}
