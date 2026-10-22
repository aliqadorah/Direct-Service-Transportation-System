using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Notifications;

namespace DirectService.Transportation.Application.Tests;

/// <summary>
/// Owner request 22 Oct 2026 (DT-253): one shared layout for every email — logo, the recipient's language first (Arabic right to left,
/// English left to right) and the other below, a button and the same link to copy, plain-text alternative, footer with the company and the
/// reply-to address, always light. Every existing email uses it.
/// </summary>
public sealed class EmailTemplateTests
{
    private const string Link = "https://app.test/reset-password?u=U1&t=TOKEN-123";

    /// <summary>Every template, so that a new one cannot skip the layout.</summary>
    public static TheoryData<string> Templates => new(All("ar").Select(m => m.Template).Distinct());

    private static IEnumerable<EmailMessage> All(string language) =>
    [
        EmailTemplates.VerifyEmail("a@example.test", "Rana", Link, language),
        EmailTemplates.VerifyAddedEmail("a@example.test", "Rana", "rana1", Link, language),
        EmailTemplates.PasswordReset("a@example.test", "Rana", Link, language),
        EmailTemplates.TwoFactorChanged("a@example.test", "Rana", "تم تفعيل التحقق", "Turned on", DateTime.UtcNow, "10.0.0.1", Link, language),
        EmailTemplates.ConfirmEmailChange("a@example.test", "Rana", true, Link, language),
        EmailTemplates.EmailChangeAsked("a@example.test", "Rana", "b•••@example.test", false, Link, language),
        EmailTemplates.EmailChanged("a@example.test", "Rana", "b•••@example.test", DateTime.UtcNow, Link, language),
        EmailTemplates.Invitation("a@example.test", "الأفق", "Sami", 7, Link, language),
        EmailTemplates.StaffInvitation("a@example.test", "Rana", "Sami", 48, Link, language),
        EmailTemplates.SupportDecision("a@example.test", "Rana", "الأفق", "Sami", DateTime.UtcNow, DateTime.UtcNow.AddHours(4), null, Link, language),
        EmailTemplates.SupportEnded("a@example.test", "Rana", "الأفق", null, DateTime.UtcNow, Link, language),
        EmailTemplates.TenantDeleted("a@example.test", "Rana", "الأفق", language),
        EmailTemplates.TestEmail("a@example.test", "Microsoft 365", "no-reply@directservicetech.com", Link, language),
        EmailTemplates.SystemEmailFailures("a@example.test", "Rana", 3, Link, language),
        EmailTemplates.SecretExpiring("a@example.test", "Rana", new DateOnly(2026, 11, 20), Link, language),
    ];

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_email_uses_the_shared_layout_in_both_languages(string template)
    {
        foreach (var language in new[] { "ar", "en" })
        {
            var m = All(language).First(x => x.Template == template);
            Assert.Equal(language, m.Language);
            Assert.NotNull(m.Content);
            // Logo, both directions with the recipient's first, always light, footer with the company.
            Assert.Contains("src=\"cid:logo\"", m.HtmlBody, StringComparison.Ordinal);
            Assert.Contains("<meta name=\"color-scheme\" content=\"light only\">", m.HtmlBody, StringComparison.Ordinal);
            Assert.Contains($"<html lang=\"{language}\" dir=\"{(language == "ar" ? "rtl" : "ltr")}\">", m.HtmlBody, StringComparison.Ordinal);
            var rtl = m.HtmlBody.IndexOf("lang=\"ar\" style", StringComparison.Ordinal);
            var ltr = m.HtmlBody.IndexOf("lang=\"en\" style", StringComparison.Ordinal);
            Assert.True(rtl > 0 && ltr > 0);
            Assert.True(language == "ar" ? rtl < ltr : ltr < rtl, "the recipient's language comes first");
            Assert.Contains(language == "ar" ? EmailLayout.CompanyAr : EmailLayout.CompanyEn, m.HtmlBody, StringComparison.Ordinal);
            Assert.Contains(language == "ar" ? EmailLayout.CompanyAr : EmailLayout.CompanyEn, m.TextBody, StringComparison.Ordinal);
            // The subject in the recipient's language first.
            Assert.StartsWith(language == "ar" ? m.Content!.SubjectAr : m.Content!.SubjectEn, m.Subject, StringComparison.Ordinal);
            // The plain-text alternative carries no markup.
            Assert.DoesNotContain("<br>", m.TextBody, StringComparison.Ordinal);
            Assert.DoesNotContain("<p", m.TextBody, StringComparison.Ordinal);
            if (m.Content!.Link is not null)
            {
                // A clear button and the same link as text to copy; the text body has the link too.
                Assert.Contains("display:inline-block;background:#0F4C5C", m.HtmlBody, StringComparison.Ordinal);
                Assert.Contains("word-break:break-all\">https://app.test/reset-password?u=U1&amp;t=TOKEN-123</p>", m.HtmlBody, StringComparison.Ordinal);
                Assert.Contains(Link, m.TextBody, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Single_use_links_are_marked_so_the_outbox_never_keeps_them_after_a_failure()
    {
        var marked = All("en").Where(m => m.HasSecretLink).Select(m => m.Template).Distinct().Order().ToList();
        Assert.Equal(["ConfirmEmailChange", "Invitation", "PasswordReset", "StaffInvitation", "VerifyEmail"], marked);
        Assert.Equal(marked, EmailTemplates.SecretLinkTemplates.Order().ToList());
    }

    [Fact]
    public void The_footer_names_the_reply_to_address_of_the_active_settings()
    {
        var m = EmailTemplates.PasswordReset("a@example.test", "Rana", Link, "en");
        Assert.Contains("replies are not read", m.TextBody, StringComparison.Ordinal);
        var (html, text) = EmailLayout.Render(m.Content!, new EmailBrand("Direct Service", "support@directservicetech.com"));
        Assert.Contains("href=\"mailto:support@directservicetech.com\"", html, StringComparison.Ordinal);
        Assert.Contains("Replies to this email go to support@directservicetech.com", text, StringComparison.Ordinal);
        var (arHtml, _) = EmailLayout.Render(EmailTemplates.PasswordReset("a@example.test", "Rana", Link, "ar").Content!, new EmailBrand("x", "support@directservicetech.com"));
        Assert.Contains("الردود على هذه الرسالة تصل إلى", arHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_typed_by_users_cannot_add_markup()
    {
        var m = EmailTemplates.Invitation("a@example.test", "<b>Co</b>", "<script>x</script>", 7, Link, "en");
        Assert.DoesNotContain("<script>", m.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", m.HtmlBody, StringComparison.Ordinal);
    }
}
