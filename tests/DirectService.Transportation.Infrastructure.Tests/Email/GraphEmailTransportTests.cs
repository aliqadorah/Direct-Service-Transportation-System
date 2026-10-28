using System.Net;
using System.Text;
using DirectService.Transportation.Application.Notifications;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Infrastructure.Tests.Email;

/// <summary>
/// System email, Microsoft 365 (DT-250): the Graph sender against a fake Microsoft sign-in and Graph (never a real credential or email) —
/// success, a refused secret (401), missing permission (403), a missing mailbox (404), throttling (429 with Retry-After); the MIME message
/// with both alternatives and the logo; header injection refused.
/// </summary>
public sealed class GraphEmailTransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly EmailTransportSettings Settings = new(EmailModes.Microsoft365, "no-reply@directservicetech.com", "Direct Service", "support@directservicetech.com",
        "11111111-2222-3333-4444-555555555555", "66666666-7777-8888-9999-000000000000", "test-secret-value");

    private static (GraphEmailTransport Transport, FakeGraph Graph) Make()
    {
        var graph = new FakeGraph();
        return (new GraphEmailTransport(new HandlerClientFactory(graph), Options.Create(new SystemEmailOptions()), TimeProvider.System, NullLogger<GraphEmailTransport>.Instance), graph);
    }

    private static OutgoingEmail Email(string to = "owner@example.test")
    {
        var m = EmailTemplates.PasswordReset(to, "Rana", "https://app.test/reset-password?u=U1&t=TOKEN-123", "en");
        return new OutgoingEmail(m.To, m.Subject, m.HtmlBody, m.TextBody, m.Template);
    }

    [Fact]
    public async Task Sends_with_graph_as_the_mailbox_with_text_html_and_the_inline_logo()
    {
        var (transport, graph) = Make();
        var result = await transport.SendAsync(Email(), Settings, Ct);
        Assert.True(result.Sent);
        Assert.Null(result.Warnings);
        var (mailbox, mime) = Assert.Single(graph.Sent);
        Assert.Equal("no-reply@directservicetech.com", mailbox);
        Assert.Contains("From: =?utf-8?B?", mime, StringComparison.Ordinal);
        Assert.Contains("<no-reply@directservicetech.com>", mime, StringComparison.Ordinal);
        Assert.Contains("Reply-To: support@directservicetech.com\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("To: owner@example.test\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("Content-Type: multipart/alternative", mime, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain; charset=utf-8", mime, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/html; charset=utf-8", mime, StringComparison.Ordinal);
        Assert.Contains("Content-ID: <logo>", mime, StringComparison.Ordinal);
        Assert.Contains("Auto-Submitted: auto-generated", mime, StringComparison.Ordinal);

        // A second email reuses the token (one sign-in per app and secret until it expires).
        Assert.True((await transport.SendAsync(Email(), Settings, Ct)).Sent);
        Assert.Equal(1, graph.TokenRequests);
    }

    [Fact]
    public async Task A_token_with_the_tenant_wide_permission_is_reported_as_a_warning()
    {
        var (transport, graph) = Make();
        graph.TenantWideMailSend = true;
        var result = await transport.SendAsync(Email(), Settings, Ct);
        Assert.True(result.Sent);
        Assert.Equal([GraphEmailTransport.TenantWideGrant], result.Warnings);
    }

    [Theory]
    [InlineData("7000215", EmailErrorKinds.WrongSecret)]       // 401 invalid_client
    [InlineData("7000222", EmailErrorKinds.SecretExpired)]
    [InlineData("700016", EmailErrorKinds.WrongClient)]
    [InlineData("90002", EmailErrorKinds.WrongTenant)]
    public async Task A_refused_sign_in_is_named_and_the_secret_never_appears(string aadsts, string kind)
    {
        var (transport, graph) = Make();
        graph.TokenRefusal = aadsts;
        var result = await transport.SendAsync(Email(), Settings, Ct);
        Assert.False(result.Sent);
        Assert.Equal(kind, result.ErrorKind);
        Assert.Contains("AADSTS" + aadsts, result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("test-secret-value", result.Detail, StringComparison.Ordinal);
        Assert.Empty(graph.Sent);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "ErrorAccessDenied", false, EmailErrorKinds.MissingPermission)]
    [InlineData(HttpStatusCode.Forbidden, "ErrorAccessDenied", true, EmailErrorKinds.MailboxNotAllowed)]
    [InlineData(HttpStatusCode.Forbidden, "ErrorSendAsDenied", false, EmailErrorKinds.MailboxNotAllowed)]
    [InlineData(HttpStatusCode.NotFound, "ErrorInvalidUser", false, EmailErrorKinds.MailboxNotFound)]
    [InlineData(HttpStatusCode.Unauthorized, "InvalidAuthenticationToken", false, EmailErrorKinds.MissingPermission)]
    [InlineData(HttpStatusCode.BadRequest, "ErrorInvalidRecipients", false, EmailErrorKinds.RecipientRejected)]
    public async Task Graph_refusals_become_plain_kinds(HttpStatusCode status, string code, bool tenantWide, string kind)
    {
        var (transport, graph) = Make();
        graph.TenantWideMailSend = tenantWide;
        graph.Answer(status, code);
        var result = await transport.SendAsync(Email(), Settings, Ct);
        Assert.False(result.Sent);
        Assert.Equal(kind, result.ErrorKind);
        Assert.Contains(code, result.Detail, StringComparison.Ordinal);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task Throttling_keeps_retry_after()
    {
        var (transport, graph) = Make();
        graph.Answer((HttpStatusCode)429, "ApplicationThrottled", TimeSpan.FromSeconds(42));
        var result = await transport.SendAsync(Email(), Settings, Ct);
        Assert.Equal(EmailErrorKinds.Throttled, result.ErrorKind);
        Assert.Equal(TimeSpan.FromSeconds(42), result.RetryAfter);

        graph.Answer(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable", TimeSpan.FromSeconds(5));
        result = await transport.SendAsync(Email(), Settings, Ct);
        Assert.Equal(EmailErrorKinds.Unavailable, result.ErrorKind);
        Assert.Equal(TimeSpan.FromSeconds(5), result.RetryAfter);
    }

    [Fact]
    public async Task No_header_can_be_injected()
    {
        var (transport, graph) = Make();
        // A recipient with a line break is refused before anything is sent.
        var result = await transport.SendAsync(Email("owner@example.test\r\nBcc: victim@example.test"), Settings, Ct);
        Assert.Equal(EmailErrorKinds.InvalidAddress, result.ErrorKind);
        Assert.Empty(graph.Sent);

        // A subject or sender name with line breaks stays one encoded header.
        var email = Email() with { Subject = "Hello\r\nBcc: victim@example.test" };
        Assert.True((await transport.SendAsync(email, Settings with { DisplayName = "DS\r\nBcc: x@example.test" }, Ct)).Sent);
        var mime = Assert.Single(graph.Sent).Mime;
        var headers = mime[..mime.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        Assert.DoesNotContain("\r\nBcc:", headers, StringComparison.Ordinal);
        var subject = MimeMessageBuilder.Encoded("Hello\r\nBcc: victim@example.test");
        Assert.DoesNotContain('\n', subject.Replace("\r\n ", "", StringComparison.Ordinal));
        Assert.Equal("HelloBcc: victim@example.test", Encoding.UTF8.GetString(Convert.FromBase64String(subject[10..^2])));
    }

    [Fact]
    public void Long_arabic_subjects_are_folded_into_words_of_at_most_75_characters()
    {
        var encoded = MimeMessageBuilder.Encoded("إعادة تعيين كلمة المرور — Reset your password — إعادة تعيين كلمة المرور مرة أخرى");
        foreach (var word in encoded.Split("\r\n ")) Assert.True(word.Length <= 75, word);
        var decoded = string.Concat(encoded.Split("\r\n ").Select(w => Encoding.UTF8.GetString(Convert.FromBase64String(w[10..^2]))));
        Assert.Equal("إعادة تعيين كلمة المرور — Reset your password — إعادة تعيين كلمة المرور مرة أخرى", decoded);
    }
}
