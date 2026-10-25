using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DirectService.Transportation.Tests.Email;

/// <summary>
/// Stands in for Microsoft sign-in and Microsoft Graph in the tests (owner rule: never a real credential, never a real email). Answers the
/// token request (a token with or without the tenant-wide "Mail.Send" role, or an AADSTS refusal) and sendMail (202, or the status, Graph
/// error code and Retry-After the test sets); records every sendMail with its decoded MIME message. Anything else answers 404.
/// </summary>
public sealed class FakeGraph : HttpMessageHandler
{
    private readonly ConcurrentQueue<(HttpStatusCode Status, string? Code, TimeSpan? RetryAfter)> _sendAnswers = new();

    /// <summary>The token request is refused with this AADSTS code (e.g. 7000215 wrong secret); null = a token is issued.</summary>
    public string? TokenRefusal { get; set; }

    /// <summary>The issued token carries the tenant-wide application permission "Mail.Send".</summary>
    public bool TenantWideMailSend { get; set; }

    /// <summary>Every sendMail: the mailbox in the address and the decoded MIME message.</summary>
    public ConcurrentQueue<(string Mailbox, string Mime)> Sent { get; } = new();

    private int _tokenRequests;

    public int TokenRequests => _tokenRequests;

    private int _held;

    /// <summary>
    /// While set, every sendMail waits for it to complete (DT-261: settings changed, or a second test sent, while a test email is on its way);
    /// <see cref="Held"/> counts the sendMail calls waiting so far.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    public int Held => Volatile.Read(ref _held);

    /// <summary>The next sendMail calls answer these, in order; afterwards 202 Accepted.</summary>
    public void Answer(HttpStatusCode status, string? code = null, TimeSpan? retryAfter = null) => _sendAnswers.Enqueue((status, code, retryAfter));

    public void Reset()
    {
        TokenRefusal = null;
        TenantWideMailSend = false;
        Hold = null;
        _held = 0;
        _sendAnswers.Clear();
        Sent.Clear();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path.Contains("/discovery/instance", StringComparison.Ordinal))
        {
            var host = request.RequestUri.Host;
            return Json(HttpStatusCode.OK, new Dictionary<string, object>
            {
                ["tenant_discovery_endpoint"] = $"https://{host}/common/v2.0/.well-known/openid-configuration", ["api-version"] = "1.1",
                ["metadata"] = new[] { new Dictionary<string, object> { ["preferred_network"] = host, ["preferred_cache"] = host, ["aliases"] = new[] { host } } },
            });
        }
        if (request.Method == HttpMethod.Post && path.EndsWith("/oauth2/v2.0/token", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _tokenRequests);
            if (TokenRefusal is { } code)
                return Json(code == "7000215" || code == "7000222" ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest, new Dictionary<string, object>
                {
                    ["error"] = code.StartsWith("700", StringComparison.Ordinal) ? "invalid_client" : "invalid_request",
                    ["error_description"] = $"AADSTS{code}: The request was refused by the test double. Trace ID: 0000",
                    ["error_codes"] = new[] { int.Parse(code, System.Globalization.CultureInfo.InvariantCulture) },
                });
            var claims = TenantWideMailSend ? "{\"aud\":\"https://graph.microsoft.com\",\"roles\":[\"Mail.Send\"]}" : "{\"aud\":\"https://graph.microsoft.com\"}";
            var token = "eyJhbGciOiJub25lIn0." + Convert.ToBase64String(Encoding.UTF8.GetBytes(claims)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";
            return Json(HttpStatusCode.OK, new Dictionary<string, object> { ["token_type"] = "Bearer", ["expires_in"] = 3599, ["ext_expires_in"] = 3599, ["access_token"] = token });
        }
        if (request.Method == HttpMethod.Post && path.EndsWith("/sendMail", StringComparison.Ordinal))
        {
            if (Hold is { } hold)
            {
                Interlocked.Increment(ref _held);
                await hold.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
            }
            var body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var mailbox = Uri.UnescapeDataString(path.Split('/')[^2]);
            if (_sendAnswers.TryDequeue(out var answer))
            {
                var r = Json(answer.Status, new Dictionary<string, object> { ["error"] = new Dictionary<string, string> { ["code"] = answer.Code ?? "Error", ["message"] = "Refused by the test double." } });
                if (answer.RetryAfter is { } wait) r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
                return r;
            }
            Sent.Enqueue((mailbox, Encoding.UTF8.GetString(Convert.FromBase64String(body))));
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"FakeGraph: no answer for {request.Method} {request.RequestUri}") };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object value) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}

/// <summary>An IHttpClientFactory whose clients all go to one handler.</summary>
public sealed class HandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
