using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using DirectService.Transportation.Application.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DirectService.Platform.Infrastructure.Email;

/// <summary>
/// Development mode: each email is written as an .eml file (the same MIME message Microsoft 365 would get, with readable 8-bit text) to the
/// drop folder. Never logs addresses.
/// </summary>
public sealed partial class FileEmailTransport(IOptions<SystemEmailOptions> options, TimeProvider clock, ILogger<FileEmailTransport> log) : IEmailTransport
{
    public string Mode => EmailModes.Development;

    public async Task<EmailDeliveryResult> SendAsync(OutgoingEmail email, EmailTransportSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(email);
        try
        {
            var mime = MimeMessageBuilder.Build(email, settings, EmailLogo.Png, clock.GetUtcNow(), eightBit: true);
            var folder = Path.GetFullPath(options.Value.DropFolder);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{clock.GetUtcNow().UtcDateTime:yyyyMMddHHmmssfff}-{email.Template}-{Guid.NewGuid():N}.eml");
            await File.WriteAllTextAsync(file, mime, new UTF8Encoding(false), ct).ConfigureAwait(false);
            LogWritten(log, email.Template);
            return EmailDeliveryResult.Ok;
        }
        catch (ArgumentException ex)
        {
            return EmailDeliveryResult.Fail(EmailErrorKinds.InvalidAddress, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return EmailDeliveryResult.Fail(EmailErrorKinds.Folder, "The development mail folder cannot be written: " + ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {Template} written to the development drop folder.")]
    private static partial void LogWritten(ILogger logger, string template);
}

/// <summary>
/// Microsoft 365 mode (DT-250): Microsoft Graph sendMail as the configured mailbox, with an access token of the Entra app (client credentials:
/// tenant ID, client ID, client secret — Azure.Identity). No SMTP, no mailbox password. The MIME message is posted as base64 (plain text and
/// HTML alternatives, the inline logo). Errors are turned into kinds the console words in plain language; 429 / 503 carry Retry-After. A
/// token that holds the tenant-wide "Mail.Send" application permission is reported as a warning: the app could then send as any mailbox.
/// </summary>
public sealed partial class GraphEmailTransport(IHttpClientFactory http, IOptions<SystemEmailOptions> options, TimeProvider clock, ILogger<GraphEmailTransport> log)
    : IEmailTransport
{
    public const string HttpClientName = "SystemEmail.Graph";
    public const string TenantWideGrant = "tenantWideGrant";

    /// <summary>One credential per app and secret: it keeps the token until it expires (a changed secret is a new credential).</summary>
    private readonly ConcurrentDictionary<string, ClientSecretCredential> _credentials = new(StringComparer.Ordinal);

    public string Mode => EmailModes.Microsoft365;

    /// <summary>Forgets every kept token (each next email asks Microsoft sign-in again); the API tests start each test with it.</summary>
    public void ClearTokens() => _credentials.Clear();

    public async Task<EmailDeliveryResult> SendAsync(OutgoingEmail email, EmailTransportSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.EntraTenantId is not { } tenant || settings.EntraClientId is not { } client || settings.ClientSecret is not { } secret)
            return EmailDeliveryResult.Fail(EmailErrorKinds.Unknown, "The Microsoft 365 settings are incomplete.");
        var o = options.Value;
        var graph = new Uri(o.GraphBaseUrl.EndsWith('/') ? o.GraphBaseUrl : o.GraphBaseUrl + "/");

        string token;
        try
        {
            var key = $"{tenant}|{client}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))}";
            ClientSecretCredential New() => new(tenant, client, secret, new ClientSecretCredentialOptions
            {
                AuthorityHost = new Uri(o.AuthorityHost),
                Transport = new HttpClientTransport(http.CreateClient(HttpClientName)),
                Retry = { MaxRetries = 0 },   // the outbox retries with its own back-off
            });
            // DT-261: a test email never uses a kept token; the new credential (and its token) then replaces the kept one. A refused
            // sign-in drops the kept one, so queued emails meet the problem too instead of riding on an old token.
            var credential = settings.FreshToken ? New() : _credentials.GetOrAdd(key, _ => New());
            try
            {
                token = (await credential.GetTokenAsync(new TokenRequestContext([graph.GetLeftPart(UriPartial.Authority) + "/.default"]), ct).ConfigureAwait(false)).Token;
            }
            catch (AuthenticationFailedException)
            {
                _credentials.TryRemove(key, out _);
                throw;
            }
            if (settings.FreshToken) _credentials[key] = credential;
        }
        catch (AuthenticationFailedException ex)
        {
            return TokenError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or Azure.RequestFailedException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return EmailDeliveryResult.Fail(EmailErrorKinds.Network, "Microsoft sign-in could not be reached: " + ex.GetType().Name);
        }

        var roles = Roles(token);
        string mime;
        try
        {
            mime = MimeMessageBuilder.Build(email, settings, EmailLogo.Png, clock.GetUtcNow(), eightBit: false);
        }
        catch (ArgumentException ex)
        {
            return EmailDeliveryResult.Fail(EmailErrorKinds.InvalidAddress, ex.Message);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(graph, $"users/{Uri.EscapeDataString(settings.SenderMailbox)}/sendMail"))
        {
            Content = new StringContent(Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)), Encoding.ASCII, "text/plain"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                LogSent(log, email.Template);
                return new EmailDeliveryResult(true, Warnings: roles.Contains("Mail.Send") ? [TenantWideGrant] : null);
            }
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = GraphError(response.StatusCode, body, RetryAfter(response), roles);
            LogRefused(log, email.Template, (int)response.StatusCode, result.ErrorKind);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return EmailDeliveryResult.Fail(EmailErrorKinds.Network, "Microsoft Graph could not be reached: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// The sign-in refused the app: AADSTS7000215 wrong secret, 7000222 expired secret, 700016 / 7000229 no such app in the tenant, 90002 /
    /// 900023 / 90072 no such tenant. The detail keeps the AADSTS code and Microsoft's first sentence (never the secret).
    /// </summary>
    internal static EmailDeliveryResult TokenError(Exception ex)
    {
        var text = ex.Message + " " + ex.InnerException?.Message;
        var code = AadstsPattern().Match(text) is { Success: true } m ? m.Groups[1].Value : null;
        var kind = code switch
        {
            "7000215" => EmailErrorKinds.WrongSecret,
            "7000222" => EmailErrorKinds.SecretExpired,
            "700016" or "7000229" or "700038" => EmailErrorKinds.WrongClient,
            "90002" or "900023" or "90072" or "90082" => EmailErrorKinds.WrongTenant,
            _ => EmailErrorKinds.Unknown,
        };
        var sentence = code is null ? null : FirstSentencePattern().Match(text[text.IndexOf("AADSTS" + code, StringComparison.Ordinal)..]).Value;
        return EmailDeliveryResult.Fail(kind, sentence ?? "Microsoft sign-in refused the app.");
    }

    /// <summary>Graph refused the email: the status and Graph's error code decide the kind; 429 / 503 keep Retry-After.</summary>
    internal static EmailDeliveryResult GraphError(HttpStatusCode status, string body, TimeSpan? retryAfter, IReadOnlySet<string> roles)
    {
        string? code = null, message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                code = e.TryGetProperty("code", out var c) ? c.GetString() : null;
                message = e.TryGetProperty("message", out var msg) ? msg.GetString() : null;
            }
        }
        catch (JsonException)
        {
        }
        var detail = $"HTTP {(int)status}{(code is null ? "" : " " + code)}{(message is null ? "" : ": " + message)}";
        var kind = ((int)status, code) switch
        {
            (429, _) => EmailErrorKinds.Throttled,
            (503 or 502 or 504 or 500, _) => EmailErrorKinds.Unavailable,
            (403, "ErrorSendAsDenied") => EmailErrorKinds.MailboxNotAllowed,
            // With the Exchange role "Application Mail.Send" scoped to a mailbox the token carries no Mail.Send role, so a 403 then means the
            // mailbox is outside the scope or the role is not assigned; with the tenant-wide permission it can only be the mailbox.
            (403, _) => roles.Contains("Mail.Send") ? EmailErrorKinds.MailboxNotAllowed : EmailErrorKinds.MissingPermission,
            (404, _) or (400, "ErrorInvalidUser" or "MailboxNotEnabledForRESTAPI" or "ResourceNotFound") => EmailErrorKinds.MailboxNotFound,
            (400, "ErrorInvalidRecipients" or "ErrorInvalidRecipientsRule") => EmailErrorKinds.RecipientRejected,
            (401, _) => EmailErrorKinds.MissingPermission,
            _ => EmailErrorKinds.Unknown,
        };
        return EmailDeliveryResult.Fail(kind, detail, kind is EmailErrorKinds.Throttled or EmailErrorKinds.Unavailable ? retryAfter : null);
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not { } r) return null;
        if (r.Delta is { } delta) return delta;
        return r.Date is { } at ? at - clock.GetUtcNow() is { Ticks: > 0 } left ? left : TimeSpan.Zero : null;
    }

    /// <summary>The application roles in the access token (read, not validated: only to warn about a tenant-wide grant).</summary>
    internal static IReadOnlySet<string> Roles(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return new HashSet<string>();
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
                ? roles.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return new HashSet<string>();
        }
    }

    [GeneratedRegex(@"AADSTS(\d+)")]
    private static partial Regex AadstsPattern();

    [GeneratedRegex(@"^AADSTS\d+:[^.]*\.")]
    private static partial Regex FirstSentencePattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {Template} sent through Microsoft Graph.")]
    private static partial void LogSent(ILogger logger, string template);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Microsoft Graph refused email {Template}: HTTP {Status} ({Kind}).")]
    private static partial void LogRefused(ILogger logger, string template, int status, string? kind);
}
