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
        $"مرحباً {E(name)}،<br>يرجى تأكيد بريدك الإلكتروني لإكمال إنشاء حسابك في Direct Service Transportation.",
        $"Hello {E(name)},<br>Please verify your email address to finish creating your Direct Service Transportation account.",
        "تأكيد البريد", "Verify email", link, nameof(VerifyEmail));

    /// <summary>
    /// Notice of a change to the user's two-step verification (DT-210): turned on, turned off, authenticator app changed, new recovery codes.
    /// <paramref name="whatAr"/>/<paramref name="whatEn"/> name the change; the time is UTC (the email has no time zone of the reader).
    /// </summary>
    public static EmailMessage TwoFactorChanged(string to, string name, string whatAr, string whatEn, DateTime atUtc, string? ip, string link)
    {
        var when = atUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        var from = string.IsNullOrWhiteSpace(ip) ? "" : $" ({E(ip)})";
        return Build(to,
            "تغيير في التحقق بخطوتين — Two-step verification changed",
            $"مرحباً {E(name)}،<br>{E(whatAr)} في حسابك في Direct Service Transportation بتاريخ {when}{from}. إن لم تكن أنت من فعل ذلك فغيّر كلمة المرور فوراً وأبلغ مسؤول المستخدمين في شركتك.",
            $"Hello {E(name)},<br>{E(whatEn)} on your Direct Service Transportation account on {when}{from}. If this was not you, change your password at once and tell your company's user administrator.",
            "مراجعة الحساب", "Review your account", link, nameof(TwoFactorChanged));
    }

    /// <summary>An email an administrator entered for a username user: confirmed only while that user is signed in (DT-203).</summary>
    public static EmailMessage VerifyAddedEmail(string to, string name, string userName, string link) => Build(to,
        "تأكيد البريد الإلكتروني — Verify your email",
        $"مرحباً {E(name)}،<br>أضاف مسؤول النظام هذا البريد إلى حسابك ({E(userName)}) في Direct Service Transportation. سجّل الدخول باسم المستخدم أولاً، ثم افتح الرابط لتأكيده. إن لم يكن هذا حسابك فتجاهل هذه الرسالة.",
        $"Hello {E(name)},<br>An administrator added this email address to your Direct Service Transportation account ({E(userName)}). Sign in with your username first, then open the link to confirm it. If this is not your account, ignore this email.",
        "تأكيد البريد", "Verify email", link, nameof(VerifyEmail));

    public static EmailMessage PasswordReset(string to, string name, string link) => Build(to,
        "إعادة تعيين كلمة المرور — Reset your password",
        $"مرحباً {E(name)}،<br>طُلبت إعادة تعيين كلمة المرور لحسابك. إن لم تطلب ذلك فتجاهل هذه الرسالة.",
        $"Hello {E(name)},<br>A password reset was requested for your account. If you did not ask for it, ignore this email.",
        "إعادة التعيين", "Reset password", link, nameof(PasswordReset));

    public static EmailMessage Invitation(string to, string companyName, string inviterName, int days, string link) => Build(to,
        $"دعوة للانضمام إلى {companyName} — Invitation to join {companyName}",
        $"دعاك {E(inviterName)} للانضمام إلى {E(companyName)} في Direct Service Transportation. صلاحية الرابط {days} أيام.",
        $"{E(inviterName)} invited you to join {E(companyName)} on Direct Service Transportation. The link is valid for {days} days.",
        "قبول الدعوة", "Accept invitation", link, nameof(Invitation));

    /// <summary>Invitation of a platform staff member (owner feedback 13 Oct 2026): a single-use link valid for <paramref name="hours"/> hours.</summary>
    public static EmailMessage StaffInvitation(string to, string name, string inviterName, int hours, string link) => Build(to,
        "دعوة إلى لوحة مزوّد الخدمة — Invitation to the provider console",
        $"مرحباً {E(name)}،<br>دعاك {E(inviterName)} للانضمام إلى فريق المنصة في Direct Service Transportation. اضغط الرابط لتعيين كلمة المرور ثم إعداد التحقق بخطوتين. الرابط يُستخدم مرة واحدة وصالح {hours} ساعة. إن لم تتوقع هذه الدعوة فتجاهلها.",
        $"Hello {E(name)},<br>{E(inviterName)} invited you to join the platform team of Direct Service Transportation. Open the link to set your password, then set up two-step verification. The link works once and is valid for {hours} hours. If you did not expect this invitation, ignore it.",
        "قبول الدعوة", "Accept invitation", link, nameof(StaffInvitation));

    /// <summary>
    /// The tenant's Owner decided a support access request (owner testing 19 Oct 2026, DT-215): the staff member who asked is told the window
    /// (UTC) and who approved it, or the refusal and its reason.
    /// </summary>
    public static EmailMessage SupportDecision(string to, string name, string tenant, string decider, DateTime? startsUtc, DateTime? endsUtc, string? refusal, string link)
    {
        static string T(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        return refusal is null
            ? Build(to,
                $"تمت الموافقة على طلب الوصول — Support access approved: {tenant}",
                $"مرحباً {E(name)}،<br>وافق {E(decider)} على طلب الوصول إلى {E(tenant)}: نافذة للقراءة فقط من {T(startsUtc)} إلى {T(endsUtc)}.",
                $"Hello {E(name)},<br>{E(decider)} approved your access request for {E(tenant)}: a read-only window from {T(startsUtc)} to {T(endsUtc)}.",
                "فتح طلبات الوصول", "Open support access", link, nameof(SupportDecision))
            : Build(to,
                $"رُفض طلب الوصول — Support access refused: {tenant}",
                $"مرحباً {E(name)}،<br>رفض {E(decider)} طلب الوصول إلى {E(tenant)}. السبب: {E(refusal)}",
                $"Hello {E(name)},<br>{E(decider)} refused your access request for {E(tenant)}. Reason: {E(refusal)}",
                "فتح طلبات الوصول", "Open support access", link, nameof(SupportDecision));
    }

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

    private static string StripTags(string s) => WebUtility.HtmlDecode(s.Replace("<br>", "\n", StringComparison.Ordinal));

    /// <summary>Names typed by users are encoded in the HTML body, so a name cannot add markup or links to an e-mail (security review 13 Oct 2026).</summary>
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
