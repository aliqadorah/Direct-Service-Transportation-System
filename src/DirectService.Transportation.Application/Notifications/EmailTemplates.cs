using System.Net;
using DirectService.Transportation.Application.Abstractions;

namespace DirectService.Transportation.Application.Notifications;

/// <summary>
/// Arabic and English email templates (INT-03, REL-09). Each email carries both languages, Arabic first
/// (RTL), so it is readable whatever the recipient's preference. Texts are placeholders until the owner
/// supplies final wording.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage VerifyEmail(string to, string name, string link) => Build(to,
        "تأكيد البريد الإلكتروني — Verify your email",
        $"مرحباً {name}،<br>يرجى تأكيد بريدك الإلكتروني لإكمال إنشاء حسابك في Direct Service Transportation.",
        $"Hello {name},<br>Please verify your email address to finish creating your Direct Service Transportation account.",
        "تأكيد البريد", "Verify email", link, nameof(VerifyEmail));

    public static EmailMessage PasswordReset(string to, string name, string link) => Build(to,
        "إعادة تعيين كلمة المرور — Reset your password",
        $"مرحباً {name}،<br>طُلبت إعادة تعيين كلمة المرور لحسابك. إن لم تطلب ذلك فتجاهل هذه الرسالة.",
        $"Hello {name},<br>A password reset was requested for your account. If you did not ask for it, ignore this email.",
        "إعادة التعيين", "Reset password", link, nameof(PasswordReset));

    public static EmailMessage Invitation(string to, string companyName, string inviterName, int days, string link) => Build(to,
        $"دعوة للانضمام إلى {companyName} — Invitation to join {companyName}",
        $"دعاك {inviterName} للانضمام إلى {companyName} في Direct Service Transportation. صلاحية الرابط {days} أيام.",
        $"{inviterName} invited you to join {companyName} on Direct Service Transportation. The link is valid for {days} days.",
        "قبول الدعوة", "Accept invitation", link, nameof(Invitation));

    private static EmailMessage Build(string to, string subject, string ar, string en, string buttonAr, string buttonEn, string link, string template)
    {
        var safeLink = WebUtility.HtmlEncode(link);
        var html =
            "<!doctype html><html><body style=\"font-family:'IBM Plex Sans Arabic',Inter,Segoe UI,Tahoma,sans-serif;color:#171A1C;background:#F6F8F7\">" +
            $"<div dir=\"rtl\" lang=\"ar\" style=\"padding:16px\"><p>{ar}</p><p><a href=\"{safeLink}\" style=\"background:#0F4C5C;color:#fff;padding:8px 16px;text-decoration:none\">{buttonAr}</a></p></div>" +
            $"<div dir=\"ltr\" lang=\"en\" style=\"padding:16px;border-top:1px solid #E3E7E6\"><p>{en}</p><p><a href=\"{safeLink}\" style=\"color:#0F4C5C\">{buttonEn}</a></p></div>" +
            "</body></html>";
        var text = $"{StripTags(ar)}\n{buttonAr}: {link}\n\n{StripTags(en)}\n{buttonEn}: {link}\n";
        return new EmailMessage(to, subject, html, text, template);
    }

    private static string StripTags(string s) => s.Replace("<br>", "\n", StringComparison.Ordinal);
}
