using System.Text;
using DirectService.Transportation.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Development drop folder for .eml files (the real provider is deferred, DEF-02).</summary>
    public string DropFolder { get; set; } = ".local/mail";

    public string From { get; set; } = "no-reply@directservice.local";
}

/// <summary>
/// Development email sender: writes each message as an .eml file to a local folder. Replaced by the
/// chosen provider through configuration (provider-neutral interface, DEF-02). Never logs addresses.
/// </summary>
public sealed partial class FileEmailSender(IOptions<EmailOptions> options, ILogger<FileEmailSender> log) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var folder = Path.GetFullPath(options.Value.DropFolder);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{message.Template}-{Guid.NewGuid():N}.eml");
        var sb = new StringBuilder()
            .Append("From: ").AppendLine(options.Value.From)
            .Append("To: ").AppendLine(message.To)
            .Append("Subject: =?utf-8?B?").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(message.Subject))).AppendLine("?=")
            .AppendLine("MIME-Version: 1.0")
            .AppendLine("Content-Type: text/html; charset=utf-8")
            .AppendLine()
            .AppendLine(message.HtmlBody);
        await File.WriteAllTextAsync(file, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
        LogWritten(log, message.Template);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {Template} written to the development drop folder.")]
    private static partial void LogWritten(ILogger logger, string template);
}
