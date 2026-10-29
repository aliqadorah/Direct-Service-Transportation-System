using System.Net;
using System.Text;

namespace DirectService.Platform.Application.Notifications;

/// <summary>One language of an email: its paragraphs (HTML, user text already encoded; "&lt;br&gt;" breaks a line) and the button's text.</summary>
public sealed record EmailSection(string BodyHtml, string? Button);

/// <summary>
/// What an email says, before the shared layout draws it (DT-253): the recipient's language first, both languages, the link the button
/// opens (none for a notice without an action) and whether that link is single-use.
/// </summary>
public sealed record EmailContent(string Language, string SubjectAr, string SubjectEn, EmailSection Ar, EmailSection En, string? Link, bool HasSecretLink)
{
    public bool English => Language == "en";

    /// <summary>The subject in the recipient's language first, then the other ("Reset your password — إعادة تعيين كلمة المرور").</summary>
    public string Subject => English ? $"{SubjectEn} — {SubjectAr}" : $"{SubjectAr} — {SubjectEn}";
}

/// <summary>Who the email is from, for the footer: the sender name and the reply-to address of the active settings.</summary>
public sealed record EmailBrand(string SenderName, string? ReplyTo)
{
    public static readonly EmailBrand Default = new(EmailLayout.ProductEn, null);
}

/// <summary>
/// The one layout of every email (owner request 22 Oct 2026, DT-253): the logo, the text in the recipient's language (Arabic right to left,
/// English left to right) and below it the other language, a clear button and the same link as text to copy, a footer with the company
/// name and the reply-to address; a plain-text alternative with the same content. Always light (a fixed white card, "color-scheme: light"):
/// emails never follow the application's dark theme. The logo is the inline image "cid:logo" the sender attaches.
/// </summary>
public static class EmailLayout
{
    public const string ProductAr = "دايركت سيرفس للنقل";
    public const string ProductEn = "Direct Service Transportation";
    public const string CompanyAr = "شركة دايركت سيرفس";
    public const string CompanyEn = "Direct Service Company";
    public const string LogoContentId = "logo";

    // Fixed light colours (the website's light palette): text 17.5:1, muted 5.9:1 on white, white on the button 9.5:1.
    private const string Page = "#F6F8F7", Card = "#FFFFFF", Line = "#E3E7E6", Ink = "#171A1C", Muted = "#5B6570", Petrol = "#0F4C5C";
    private const string FontAr = "'IBM Plex Sans Arabic',Tahoma,'Segoe UI',Arial,sans-serif";
    private const string FontEn = "Inter,'Segoe UI',Arial,sans-serif";

    /// <summary>The HTML and plain-text bodies of <paramref name="content"/> with <paramref name="brand"/>'s footer.</summary>
    public static (string Html, string Text) Render(EmailContent content, EmailBrand brand)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(brand);
        var en = content.English;
        var html = new StringBuilder()
            .Append("<!doctype html><html lang=\"").Append(en ? "en" : "ar").Append("\" dir=\"").Append(en ? "ltr" : "rtl").Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"color-scheme\" content=\"light only\"><meta name=\"supported-color-schemes\" content=\"light\">")
            .Append("<title>").Append(E(content.Subject)).Append("</title></head>\n")
            .Append("<body style=\"margin:0;padding:0;background:").Append(Page).Append(";color:").Append(Ink).Append(";color-scheme:light\">\n")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:").Append(Page).Append("\"><tr><td align=\"center\" style=\"padding:24px 12px\">\n")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;background:").Append(Card).Append(";border:1px solid ").Append(Line).Append(";border-radius:8px\">\n")
            .Append("<tr><td dir=\"").Append(en ? "ltr" : "rtl").Append("\" style=\"padding:18px 24px;border-bottom:1px solid ").Append(Line).Append(";font-family:").Append(en ? FontEn : FontAr).Append("\">")
            .Append("<img src=\"cid:").Append(LogoContentId).Append("\" width=\"28\" height=\"28\" alt=\"\" style=\"vertical-align:middle;border:0\"> ")
            .Append("<span style=\"vertical-align:middle;font-size:16px;font-weight:600;color:").Append(Petrol).Append("\">").Append(en ? ProductEn : ProductAr).Append("</span></td></tr>\n");
        Section(html, content, en, primary: true);
        Section(html, content, !en, primary: false);
        html.Append("<tr><td dir=\"").Append(en ? "ltr" : "rtl").Append("\" style=\"padding:16px 24px;border-top:1px solid ").Append(Line).Append(";font-family:").Append(en ? FontEn : FontAr)
            .Append(";font-size:12px;line-height:1.6;color:").Append(Muted).Append("\">")
            .Append(E(en ? CompanyEn : CompanyAr)).Append(" · ").Append(E(en ? ProductEn : ProductAr));
        if (brand.ReplyTo is { Length: > 0 } reply)
            html.Append("<br>").Append(en ? "Replies to this email go to " : "الردود على هذه الرسالة تصل إلى ")
                .Append("<a href=\"mailto:").Append(E(reply)).Append("\" style=\"color:").Append(Petrol).Append("\" dir=\"ltr\">").Append(E(reply)).Append("</a>");
        else
            html.Append("<br>").Append(en ? "This is an automatic email; replies are not read." : "هذه رسالة تلقائية؛ لا تُقرأ الردود عليها.");
        html.Append("</td></tr>\n</table></td></tr></table>\n</body></html>\n");

        var text = new StringBuilder();
        Text(text, content, en);
        text.AppendLine().AppendLine("— — —").AppendLine();
        Text(text, content, !en);
        text.AppendLine().AppendLine("— — —")
            .Append(en ? CompanyEn : CompanyAr).Append(" · ").AppendLine(en ? ProductEn : ProductAr)
            .AppendLine(brand.ReplyTo is { Length: > 0 } r
                ? (en ? "Replies to this email go to " : "الردود على هذه الرسالة تصل إلى ") + r
                : en ? "This is an automatic email; replies are not read." : "هذه رسالة تلقائية؛ لا تُقرأ الردود عليها.");
        return (html.ToString(), text.ToString());
    }

    /// <summary>One language's block: text, the button and the link to copy (the first language), a plain link (the second).</summary>
    private static void Section(StringBuilder html, EmailContent content, bool en, bool primary)
    {
        var s = en ? content.En : content.Ar;
        html.Append("<tr><td dir=\"").Append(en ? "ltr" : "rtl").Append("\" lang=\"").Append(en ? "en" : "ar").Append("\" style=\"padding:")
            .Append(primary ? "24px 24px 20px" : "16px 24px 20px;border-top:1px solid " + Line).Append(";font-family:").Append(en ? FontEn : FontAr)
            .Append(";font-size:").Append(primary ? "15px" : "13px").Append(";line-height:1.7;color:").Append(primary ? Ink : Muted).Append(";text-align:").Append(en ? "left" : "right").Append("\">")
            .Append("<p style=\"margin:0 0 16px\">").Append(s.BodyHtml).Append("</p>");
        if (content.Link is { } link && s.Button is { } button)
        {
            var href = E(link);
            if (primary)
            {
                html.Append("<p style=\"margin:0 0 16px\"><a href=\"").Append(href).Append("\" style=\"display:inline-block;background:").Append(Petrol)
                    .Append(";color:#FFFFFF;padding:11px 22px;border-radius:6px;text-decoration:none;font-weight:600\">").Append(E(button)).Append("</a></p>")
                    .Append("<p style=\"margin:0 0 6px;font-size:13px;color:").Append(Muted).Append("\">")
                    .Append(en ? "If the button does not open, copy this link into your browser:" : "إن لم يفتح الزر فانسخ هذا الرابط والصقه في المتصفح:").Append("</p>")
                    .Append("<p dir=\"ltr\" style=\"margin:0;padding:8px 10px;background:").Append(Page).Append(";border:1px solid ").Append(Line)
                    .Append(";border-radius:4px;font-family:Consolas,'Courier New',monospace;font-size:12px;text-align:left;word-break:break-all\">").Append(href).Append("</p>");
            }
            else
            {
                html.Append("<p style=\"margin:0\"><a href=\"").Append(href).Append("\" style=\"color:").Append(Petrol).Append("\">").Append(E(button)).Append("</a></p>");
            }
        }
        html.Append("</td></tr>\n");
    }

    private static void Text(StringBuilder text, EmailContent content, bool en)
    {
        var s = en ? content.En : content.Ar;
        text.AppendLine(WebUtility.HtmlDecode(s.BodyHtml.Replace("<br>", "\n", StringComparison.Ordinal)));
        if (content.Link is { } link && s.Button is { } button) text.AppendLine().Append(button).Append(": ").AppendLine(link);
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
