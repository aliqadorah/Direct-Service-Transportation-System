using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Endpoints;

namespace DirectService.Transportation.Web.Pipeline;

/// <summary>
/// The server-rendered account pages after an antiforgery refusal (owner testing 20 Oct 2026, DT-221). The framework answers a form posted
/// with a missing or outdated token (page opened before the user signed in or out in another tab, an expired browser session, a
/// cross-site post) with a bare 400 and a developer text. Here the refused post never reaches the page's handler: it is turned into a
/// plain GET of the same address, so the page renders again with a fresh token and <see cref="ExpiredForm"/> tells it to say "This page
/// expired. Please try again." The email, username and names typed in are kept — only for a same-site post, never a password, code or
/// hidden field. Antiforgery validation itself is unchanged (it runs in <c>UseAntiforgery</c> before this).
/// </summary>
internal sealed partial class ExpiredFormMiddleware(RequestDelegate next, ILogger<ExpiredFormMiddleware> logger)
{
    /// <summary>The form fields kept for the new page (last part of the field name, e.g. <c>Input.Email</c>).</summary>
    private static readonly HashSet<string> Kept = new(StringComparer.Ordinal) { "Email", "FullName", "OrganisationName" };

    public async Task InvokeAsync(HttpContext http)
    {
        if (HttpMethods.IsPost(http.Request.Method)
            && http.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: false }
            && http.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is not null)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var refused = http.Features.Get<IAntiforgeryValidationFeature>();
            if (SameSite(http))
            {
                // The framework refuses to read a form whose token failed until the caller has looked at the result. It has been looked at:
                // the form is read once here for the kept fields only, then the refusal is put back so nothing else can read it.
                http.Features.Set<IAntiforgeryValidationFeature>(Checked.Instance);
                try
                {
                    if (http.Request.HasFormContentType)
                        foreach (var (name, value) in await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false))
                            if (Kept.Contains(name[(name.LastIndexOf('.') + 1)..]) && value.ToString() is { Length: > 0 and <= 256 } v) values[name] = v;
                }
                finally
                {
                    http.Features.Set(refused);
                }
            }
            http.Features.Set(new ExpiredForm(values));
            http.Request.Method = HttpMethods.Get;
            LogExpired(logger, http.Request.Path);
        }
        await next(http).ConfigureAwait(false);
    }

    /// <summary>A post from this site (or a browser that sends no fetch metadata); a cross-site post keeps nothing.</summary>
    private static bool SameSite(HttpContext http) =>
        http.Request.Headers["Sec-Fetch-Site"].ToString() is "" or "same-origin" or "none";

    private sealed class Checked : IAntiforgeryValidationFeature
    {
        public static readonly Checked Instance = new();
        public bool IsValid => true;
        public Exception? Error => null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account form at {Path} posted with a missing or outdated antiforgery token; the page is shown again.")]
    private static partial void LogExpired(ILogger logger, PathString path);
}

/// <summary>Set on a request whose form was refused for its antiforgery token: the page says it expired and refills these fields.</summary>
public sealed class ExpiredForm(IReadOnlyDictionary<string, string> values)
{
    /// <summary>A kept field by its form name (<c>Input.Email</c>), else null.</summary>
    public string? Value(string field) => values.TryGetValue(field, out var v) ? v : null;
}
