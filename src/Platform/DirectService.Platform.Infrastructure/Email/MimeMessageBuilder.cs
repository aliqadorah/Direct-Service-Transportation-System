using System.Globalization;
using System.Text;
using DirectService.Transportation.Application.Platform;

namespace DirectService.Platform.Infrastructure.Email;

/// <summary>
/// The email as one MIME message (RFC 5322 / 2045–2047), the same for every mode: multipart/alternative with the plain text and, related to it,
/// the HTML with the inline logo ("cid:logo"). Header values come only from validated addresses; the sender name and the subject are
/// encoded words with every control character removed, so no value can add a header (header injection). Marked as an automatic email
/// (Auto-Submitted, X-Auto-Response-Suppress) so out-of-office replies do not answer it. Base64 parts for Microsoft 365; 8-bit text in the
/// development files so they stay readable.
/// </summary>
internal static class MimeMessageBuilder
{
    private const string Crlf = "\r\n";

    public static string Build(OutgoingEmail email, EmailTransportSettings settings, byte[] logo, DateTimeOffset date, bool eightBit)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logo);
        var from = Address(settings.SenderMailbox);
        var to = Address(email.To);
        var replyTo = settings.ReplyTo is { Length: > 0 } r ? Address(r) : null;
        var domain = from[(from.LastIndexOf('@') + 1)..];
        var alt = "alt-" + Guid.NewGuid().ToString("N");
        var rel = "rel-" + Guid.NewGuid().ToString("N");
        var cte = eightBit ? "8bit" : "base64";

        var sb = new StringBuilder()
            .Append("From: ").Append(Encoded(settings.DisplayName)).Append(" <").Append(from).Append('>').Append(Crlf);
        if (replyTo is not null) sb.Append("Reply-To: ").Append(replyTo).Append(Crlf);
        sb.Append("To: ").Append(to).Append(Crlf)
            .Append("Subject: ").Append(Encoded(email.Subject)).Append(Crlf)
            .Append("Date: ").Append(date.ToString("ddd, dd MMM yyyy HH:mm:ss +0000", CultureInfo.InvariantCulture)).Append(Crlf)
            .Append("Message-ID: <").Append(Guid.NewGuid().ToString("N")).Append('@').Append(domain).Append('>').Append(Crlf)
            .Append("MIME-Version: 1.0").Append(Crlf)
            .Append("Auto-Submitted: auto-generated").Append(Crlf)
            .Append("X-Auto-Response-Suppress: All").Append(Crlf)
            .Append("Content-Type: multipart/alternative; boundary=\"").Append(alt).Append('"').Append(Crlf)
            .Append(Crlf)
            .Append("--").Append(alt).Append(Crlf)
            .Append("Content-Type: text/plain; charset=utf-8").Append(Crlf)
            .Append("Content-Transfer-Encoding: ").Append(cte).Append(Crlf).Append(Crlf)
            .Append(Body(email.Text, eightBit)).Append(Crlf)
            .Append("--").Append(alt).Append(Crlf)
            .Append("Content-Type: multipart/related; boundary=\"").Append(rel).Append("\"; type=\"text/html\"").Append(Crlf).Append(Crlf)
            .Append("--").Append(rel).Append(Crlf)
            .Append("Content-Type: text/html; charset=utf-8").Append(Crlf)
            .Append("Content-Transfer-Encoding: ").Append(cte).Append(Crlf).Append(Crlf)
            .Append(Body(email.Html, eightBit)).Append(Crlf)
            .Append("--").Append(rel).Append(Crlf)
            .Append("Content-Type: image/png; name=\"logo.png\"").Append(Crlf)
            .Append("Content-Transfer-Encoding: base64").Append(Crlf)
            .Append("Content-ID: <").Append(DirectService.Transportation.Application.Notifications.EmailLayout.LogoContentId).Append('>').Append(Crlf)
            .Append("Content-Disposition: inline; filename=\"logo.png\"").Append(Crlf).Append(Crlf)
            .Append(Base64Lines(logo)).Append(Crlf)
            .Append("--").Append(rel).Append("--").Append(Crlf)
            .Append("--").Append(alt).Append("--").Append(Crlf);
        return sb.ToString();
    }

    /// <summary>A plain address, checked again here: anything that is not one (a line break, a display name, a list) is refused.</summary>
    internal static string Address(string value)
    {
        var a = value.Trim();
        if (!SystemEmailService.IsAddress(a)) throw new ArgumentException("Not a single plain email address.", nameof(value));
        return a;
    }

    /// <summary>RFC 2047 encoded words (UTF-8, base64) of at most 75 characters, folded; control characters are removed first.</summary>
    internal static string Encoded(string value)
    {
        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) return "\"\"";
        var words = new List<string>();
        var chunk = new StringBuilder();
        foreach (var rune in clean.EnumerateRunes())
        {
            // 45 bytes of UTF-8 → 60 base64 characters + 12 for "=?utf-8?B?…?=" ≤ 75 (never splitting a character).
            if (Encoding.UTF8.GetByteCount(chunk.ToString() + rune) > 45)
            {
                words.Add(Word(chunk.ToString()));
                chunk.Clear();
            }
            chunk.Append(rune.ToString());
        }
        if (chunk.Length > 0) words.Add(Word(chunk.ToString()));
        return string.Join(Crlf + " ", words);

        static string Word(string s) => "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) + "?=";
    }

    private static string Body(string text, bool eightBit) =>
        eightBit ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", Crlf, StringComparison.Ordinal) : Base64Lines(Encoding.UTF8.GetBytes(text));

    private static string Base64Lines(byte[] bytes) => Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks);
}

/// <summary>The logo every email carries (embedded in this assembly).</summary>
internal static class EmailLogo
{
    /// <summary>The embedded resource's LogicalName (the csproj's EmbeddedResource; pinned, platform design §e, E-32).</summary>
    public const string ResourceName = "DST.Email.Logo.png";

    public static readonly byte[] Png = Load();

    private static byte[] Load()
    {
        using var s = typeof(EmailLogo).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException("Email logo resource missing.");
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }
}
