using System.Net;
using DirectService.Transportation.Application.Abstractions;

namespace DirectService.Transportation.Application.Notifications;

/// <summary>
/// Arabic and English email templates (INT-03, REL-09), all drawn by the one shared layout (<see cref="EmailLayout"/>, DT-253): the text in the
/// recipient's language (<c>language</c>: "ar" / "en") first, then the other language, so it stays readable whatever the preference. Emails
/// with a single-use link (verification, password reset, invitations, email change) are marked, so the outbox never keeps them after a failure.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage VerifyEmail(string to, string name, string link, string language = "ar") => Build(to,
        "تأكيد البريد الإلكتروني", "Verify your email",
        $"مرحباً {E(name)}،<br>يرجى تأكيد بريدك الإلكتروني لإكمال إنشاء حسابك في Direct Service Transportation.",
        $"Hello {E(name)},<br>Please verify your email address to finish creating your Direct Service Transportation account.",
        "تأكيد البريد", "Verify email", link, EmailTemplateNames.VerifyEmail, language, secret: true);

    /// <summary>
    /// Notice of a change to the user's two-step verification (DT-210): turned on, turned off, authenticator app changed, new recovery codes.
    /// <paramref name="whatAr"/>/<paramref name="whatEn"/> name the change; the time is UTC (the email has no time zone of the reader).
    /// </summary>
    public static EmailMessage TwoFactorChanged(string to, string name, string whatAr, string whatEn, DateTime atUtc, string? ip, string link, string language = "ar")
    {
        var when = atUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        var from = string.IsNullOrWhiteSpace(ip) ? "" : $" ({E(ip)})";
        return Build(to,
            "تغيير في التحقق بخطوتين", "Two-step verification changed",
            $"مرحباً {E(name)}،<br>{E(whatAr)} في حسابك في Direct Service Transportation بتاريخ {when}{from}. إن لم تكن أنت من فعل ذلك فغيّر كلمة المرور فوراً وأبلغ مسؤول المستخدمين في شركتك.",
            $"Hello {E(name)},<br>{E(whatEn)} on your Direct Service Transportation account on {when}{from}. If this was not you, change your password at once and tell your company's user administrator.",
            "مراجعة الحساب", "Review your account", link, EmailTemplateNames.TwoFactorChanged, language);
    }

    /// <summary>
    /// DT-229: confirm a new email address for an existing account (asked by the user, or started by an administrator). The change takes effect
    /// only when the account's user, signed in, confirms it; until then sign-in and password reset keep the current address.
    /// </summary>
    public static EmailMessage ConfirmEmailChange(string to, string name, bool byAdministrator, string link, string language = "ar") => Build(to,
        "تأكيد تغيير البريد الإلكتروني", "Confirm your new email",
        byAdministrator
            ? $"مرحباً {E(name)}،<br>طلب مسؤول المستخدمين جعل هذا البريد بريد حسابك في Direct Service Transportation. لا يتغيّر شيء حتى تسجّل الدخول إلى حسابك وتفتح الرابط لتأكيده. إن لم يكن هذا حسابك فتجاهل هذه الرسالة."
            : $"مرحباً {E(name)}،<br>طلبت جعل هذا البريد بريد حسابك في Direct Service Transportation. سجّل الدخول وافتح الرابط لتأكيده؛ حتى ذلك الحين يبقى بريدك الحالي. إن لم تطلب ذلك فتجاهل هذه الرسالة.",
        byAdministrator
            ? $"Hello {E(name)},<br>Your user administrator asked to make this the email address of your Direct Service Transportation account. Nothing changes until you sign in to your account and open the link to confirm it. If this is not your account, ignore this email."
            : $"Hello {E(name)},<br>You asked to make this the email address of your Direct Service Transportation account. Sign in and open the link to confirm it; until then your current address stays. If you did not ask for this, ignore this email.",
        "تأكيد البريد الجديد", "Confirm new email", link, EmailTemplateNames.ConfirmEmailChange, language, secret: true);

    /// <summary>DT-229 (security review F8): notice to the current address as soon as a change is asked (the new address masked).</summary>
    public static EmailMessage EmailChangeAsked(string to, string name, string maskedNew, bool byAdministrator, string link, string language = "ar") => Build(to,
        "طلب تغيير بريدك الإلكتروني", "Email change requested",
        (byAdministrator ? $"مرحباً {E(name)}،<br>بدأ مسؤول المستخدمين تغيير بريد حسابك في Direct Service Transportation إلى {E(maskedNew)}." : $"مرحباً {E(name)}،<br>طُلب تغيير بريد حسابك في Direct Service Transportation إلى {E(maskedNew)}.")
            + " لا يتغيّر شيء قبل تأكيده من حسابك. إن لم تكن أنت من طلب ذلك فسجّل الدخول وألغِ الطلب من ملفك الشخصي وغيّر كلمة المرور، وتواصل مع مسؤول المستخدمين.",
        (byAdministrator ? $"Hello {E(name)},<br>Your user administrator started changing the email address of your Direct Service Transportation account to {E(maskedNew)}." : $"Hello {E(name)},<br>A change of the email address of your Direct Service Transportation account to {E(maskedNew)} was requested.")
            + " Nothing changes until it is confirmed from your account. If this wasn't you, sign in, cancel the request in your profile, change your password and contact your administrator.",
        "تسجيل الدخول", "Sign in", link, EmailTemplateNames.EmailChangeAsked, language);

    /// <summary>DT-229: notice to the previous address after the change (the new address is shown masked).</summary>
    public static EmailMessage EmailChanged(string to, string name, string maskedNew, DateTime atUtc, string link, string language = "ar") => Build(to,
        "تم تغيير بريدك الإلكتروني", "Your email was changed",
        $"مرحباً {E(name)}،<br>تم تغيير بريد حسابك في Direct Service Transportation إلى {E(maskedNew)} بتاريخ {atUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)} UTC. إن لم تكن أنت من فعل ذلك فتواصل مع مسؤول المستخدمين في شركتك فوراً.",
        $"Hello {E(name)},<br>The email address of your Direct Service Transportation account was changed to {E(maskedNew)} on {atUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)} UTC. If this wasn't you, contact your administrator at once.",
        "تسجيل الدخول", "Sign in", link, EmailTemplateNames.EmailChanged, language);

    /// <summary>An email an administrator entered for a username user: confirmed only while that user is signed in (DT-203).</summary>
    public static EmailMessage VerifyAddedEmail(string to, string name, string userName, string link, string language = "ar") => Build(to,
        "تأكيد البريد الإلكتروني", "Verify your email",
        $"مرحباً {E(name)}،<br>أضاف مسؤول النظام هذا البريد إلى حسابك ({E(userName)}) في Direct Service Transportation. سجّل الدخول باسم المستخدم أولاً، ثم افتح الرابط لتأكيده. إن لم يكن هذا حسابك فتجاهل هذه الرسالة.",
        $"Hello {E(name)},<br>An administrator added this email address to your Direct Service Transportation account ({E(userName)}). Sign in with your username first, then open the link to confirm it. If this is not your account, ignore this email.",
        "تأكيد البريد", "Verify email", link, EmailTemplateNames.VerifyEmail, language, secret: true);

    public static EmailMessage PasswordReset(string to, string name, string link, string language = "ar") => Build(to,
        "إعادة تعيين كلمة المرور", "Reset your password",
        $"مرحباً {E(name)}،<br>طُلبت إعادة تعيين كلمة المرور لحسابك. إن لم تطلب ذلك فتجاهل هذه الرسالة.",
        $"Hello {E(name)},<br>A password reset was requested for your account. If you did not ask for it, ignore this email.",
        "إعادة التعيين", "Reset password", link, EmailTemplateNames.PasswordReset, language, secret: true);

    public static EmailMessage Invitation(string to, string companyName, string inviterName, int days, string link, string language = "ar") => Build(to,
        $"دعوة للانضمام إلى {companyName}", $"Invitation to join {companyName}",
        $"دعاك {E(inviterName)} للانضمام إلى {E(companyName)} في Direct Service Transportation. صلاحية الرابط {days} أيام.",
        $"{E(inviterName)} invited you to join {E(companyName)} on Direct Service Transportation. The link is valid for {days} days.",
        "قبول الدعوة", "Accept invitation", link, EmailTemplateNames.Invitation, language, secret: true);

    /// <summary>Invitation of a platform staff member (owner feedback 13 Oct 2026): a single-use link valid for <paramref name="hours"/> hours.</summary>
    public static EmailMessage StaffInvitation(string to, string name, string inviterName, int hours, string link, string language = "ar") => Build(to,
        "دعوة إلى لوحة مزوّد الخدمة", "Invitation to the provider console",
        $"مرحباً {E(name)}،<br>دعاك {E(inviterName)} للانضمام إلى فريق المنصة في Direct Service Transportation. اضغط الرابط لتعيين كلمة المرور ثم إعداد التحقق بخطوتين. الرابط يُستخدم مرة واحدة وصالح {hours} ساعة. إن لم تتوقع هذه الدعوة فتجاهلها.",
        $"Hello {E(name)},<br>{E(inviterName)} invited you to join the platform team of Direct Service Transportation. Open the link to set your password, then set up two-step verification. The link works once and is valid for {hours} hours. If you did not expect this invitation, ignore it.",
        "قبول الدعوة", "Accept invitation", link, EmailTemplateNames.StaffInvitation, language, secret: true);

    /// <summary>
    /// The tenant's Owner decided a support access request (owner testing 19 Oct 2026, DT-215): the staff member who asked is told the window
    /// (UTC) and who approved it, or the refusal and its reason.
    /// </summary>
    public static EmailMessage SupportDecision(string to, string name, string tenant, string decider, DateTime? startsUtc, DateTime? endsUtc, string? refusal, string link,
        string language = "ar")
    {
        static string T(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        return refusal is null
            ? Build(to,
                $"تمت الموافقة على طلب الوصول: {tenant}", $"Support access approved: {tenant}",
                $"مرحباً {E(name)}،<br>وافق {E(decider)} على طلب الوصول إلى {E(tenant)}: نافذة للقراءة فقط من {T(startsUtc)} إلى {T(endsUtc)}.",
                $"Hello {E(name)},<br>{E(decider)} approved your access request for {E(tenant)}: a read-only window from {T(startsUtc)} to {T(endsUtc)}.",
                "فتح طلبات الوصول", "Open support access", link, EmailTemplateNames.SupportDecision, language)
            : Build(to,
                $"رُفض طلب الوصول: {tenant}", $"Support access refused: {tenant}",
                $"مرحباً {E(name)}،<br>رفض {E(decider)} طلب الوصول إلى {E(tenant)}. السبب: {E(refusal)}",
                $"Hello {E(name)},<br>{E(decider)} refused your access request for {E(tenant)}. Reason: {E(refusal)}",
                "فتح طلبات الوصول", "Open support access", link, EmailTemplateNames.SupportDecision, language);
    }

    /// <summary>
    /// A support window of the staff member ended (owner testing 20 Oct 2026, DT-220): ended early by the subscriber (<paramref name="endedBy"/>
    /// names who) or expired; the time is UTC.
    /// </summary>
    public static EmailMessage SupportEnded(string to, string name, string tenant, string? endedBy, DateTime atUtc, string link, string language = "ar")
    {
        var when = atUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        return endedBy is not null
            ? Build(to,
                $"أنهى المشترك جلسة الدعم: {tenant}", $"Support session ended by the subscriber: {tenant}",
                $"مرحباً {E(name)}،<br>أنهى {E(endedBy)} جلسة الدعم في {E(tenant)} بتاريخ {when}. لم يعد بإمكانك الاطلاع على بياناته.",
                $"Hello {E(name)},<br>{E(endedBy)} ended your support session for {E(tenant)} on {when}. You can no longer view its data.",
                "فتح طلبات الوصول", "Open support access", link, EmailTemplateNames.SupportEnded, language)
            : Build(to,
                $"انتهت نافذة الدعم: {tenant}", $"Support window ended: {tenant}",
                $"مرحباً {E(name)}،<br>انتهت نافذة الدعم في {E(tenant)} بتاريخ {when}. اطلب نافذة جديدة إن احتجت.",
                $"Hello {E(name)},<br>Your support window for {E(tenant)} ended on {when}. Request a new window if you need one.",
                "فتح طلبات الوصول", "Open support access", link, EmailTemplateNames.SupportEnded, language);
    }

    /// <summary>The tenant's retention period ended and its data was deleted (5.2, SEC-19): a notice to each Owner, without a link.</summary>
    public static EmailMessage TenantDeleted(string to, string name, string tenant, string language = "ar") => Build(to,
        "تم حذف بياناتكم", "Your data has been deleted",
        $"مرحباً {E(name)}،<br>انتهت فترة الاحتفاظ ببيانات {E(tenant)} ولم تعد متاحة.",
        $"Dear {E(name)},<br>The retention period of {E(tenant)} has ended; its data is no longer accessible.",
        null, null, null, EmailTemplateNames.TenantDeleted, language);

    /// <summary>System email (DT-250): the test email platform staff send from the console to check the settings.</summary>
    public static EmailMessage TestEmail(string to, string mode, string mailbox, string link, string language = "ar") => Build(to,
        "رسالة تجريبية من البريد الإلكتروني للنظام", "System email test",
        $"هذه رسالة تجريبية من لوحة مزوّد الخدمة. وصولها يعني أن إعدادات البريد ({E(mode)}، المرسل {E(mailbox)}) تعمل.",
        $"This is a test email from the provider console. Its arrival means the email settings ({E(mode)}, sender {E(mailbox)}) work.",
        "فتح إعدادات البريد", "Open the email settings", link, EmailTemplateNames.TestEmail, language);

    /// <summary>System email (DT-252): to platform staff — <paramref name="failed"/> emails of the last 24 hours could not be sent after the last attempt.</summary>
    public static EmailMessage SystemEmailFailures(string to, string name, int failed, string link, string language = "ar") => Build(to,
        "رسائل بريد لم تُرسل", "Emails could not be sent",
        $"مرحباً {E(name)}،<br>لم تُرسل {failed} رسالة بريد خلال آخر 24 ساعة بعد آخر محاولة. افتح سجل الإرسال في لوحة مزوّد الخدمة لمعرفة السبب وإعادة الإرسال.",
        $"Hello {E(name)},<br>{failed} email(s) of the last 24 hours could not be sent after the last attempt. Open the delivery log in the provider console to see why and resend.",
        "فتح سجل الإرسال", "Open the delivery log", link, EmailTemplateNames.SystemEmailFailures, language);

    /// <summary>System email (DT-252): to platform staff — the Microsoft 365 app's client secret expires on <paramref name="expiresOn"/> (30 and 7 days before).</summary>
    public static EmailMessage SecretExpiring(string to, string name, DateOnly expiresOn, string link, string language = "ar")
    {
        var day = expiresOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return Build(to,
            "ينتهي سرّ تطبيق البريد قريباً", "The email app's secret expires soon",
            $"مرحباً {E(name)}،<br>ينتهي سرّ تطبيق Microsoft 365 المستخدم لإرسال بريد النظام بتاريخ {day}. أنشئ سرّاً جديداً وأدخله في لوحة مزوّد الخدمة قبل ذلك، وإلا توقف إرسال البريد.",
            $"Hello {E(name)},<br>The client secret of the Microsoft 365 app that sends the system email expires on {day}. Create a new secret and enter it in the provider console before then, or emails stop being sent.",
            "فتح إعدادات البريد", "Open the email settings", link, EmailTemplateNames.SecretExpiring, language);
    }

    /// <summary>Templates whose link works once (verification, password reset, invitations, email change).</summary>
    public static readonly IReadOnlySet<string> SecretLinkTemplates = new HashSet<string>(StringComparer.Ordinal)
    {
        EmailTemplateNames.VerifyEmail, EmailTemplateNames.PasswordReset, EmailTemplateNames.ConfirmEmailChange, EmailTemplateNames.Invitation, EmailTemplateNames.StaffInvitation,
    };

    private static EmailMessage Build(string to, string subjectAr, string subjectEn, string ar, string en, string? buttonAr, string? buttonEn, string? link, string template,
        string language, bool secret = false)
    {
        var content = new EmailContent(language == "en" ? "en" : "ar", subjectAr, subjectEn, new EmailSection(ar, buttonAr), new EmailSection(en, buttonEn), link, secret);
        var (html, text) = EmailLayout.Render(content, EmailBrand.Default);
        return new EmailMessage(to, content.Subject, html, text, template, content.Language, content, secret);
    }

    /// <summary>Names typed by users are encoded in the HTML body, so a name cannot add markup or links to an e-mail (security review 13 Oct 2026).</summary>
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
