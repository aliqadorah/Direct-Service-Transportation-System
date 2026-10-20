using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Web.Client.Localization;
using DirectService.Transportation.Web.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.RateLimiting;

namespace DirectService.Transportation.Web.Components.Account;

/// <summary>
/// Base of the server-rendered account pages (ADR-02): no WebAssembly before sign-in, rate-limited like <c>/api/v1/auth</c> (SEC-14),
/// excluded from interactive routing, and business refusals shown with the same messages and field placement as the API.
/// </summary>
[ExcludeFromInteractiveRouting]
[EnableRateLimiting(RateLimits.Auth)]
[Layout(typeof(DirectService.Transportation.Web.Client.Layout.BareLayout))]
public abstract class AccountPage : ComponentBase
{
    [CascadingParameter] protected HttpContext Http { get; set; } = default!;
    [Inject] protected Loc L { get; set; } = default!;
    [Inject] protected IRequestContext Request { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    /// <summary>The page renders in the request's language (profile, ?lang= switch or browser) from its first line.</summary>
    public override Task SetParametersAsync(ParameterView parameters)
    {
        L.SetLanguage(Request.Language);
        return base.SetParametersAsync(parameters);
    }

    /// <summary>Problem details of a refusal, in the page language (field errors with their messages).</summary>
    protected ApiProblem Problem(AppException e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var lang = Request.Language;
        return new ApiProblem
        {
            Status = e.Status,
            Code = e.Code,
            Field = e.Field,
            Detail = ErrorCatalog.Format(e.Code, lang, e.Args),
            TraceId = Http.TraceIdentifier,
            Errors = e.Errors.Count == 0 ? null : e.Errors.Select(f => new ApiFieldError(f.Field, f.Code, ErrorCatalog.Format(f.Code, lang, f.Args))).ToList(),
        };
    }

    /// <summary>
    /// A field typed into a form that was refused for an outdated antiforgery token (DT-221), by its form name (<c>Input.Email</c>), to
    /// fill the page shown again; null otherwise. Only emails, usernames and names are kept, never passwords or codes.
    /// </summary>
    protected string? Kept(string field) => Http.Features.Get<DirectService.Transportation.Web.Pipeline.ExpiredForm>()?.Value(field);

    /// <summary>A return address inside this application only (no open redirect).</summary>
    protected static string LocalUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } r && r[0] == '/' && !r.StartsWith("//", StringComparison.Ordinal) && !r.StartsWith("/\\", StringComparison.Ordinal) ? r : "/";

    /// <summary>Adds the language switch to links between account pages.</summary>
    protected string Href(string path, string? returnUrl = null)
    {
        var q = new List<string>();
        if (Http.Request.Query["lang"].ToString() is "ar" or "en") q.Add("lang=" + Http.Request.Query["lang"]);
        if (returnUrl is not null && returnUrl != "/") q.Add("returnUrl=" + Uri.EscapeDataString(returnUrl));
        // An expired form of a later sign-in step sends the user back to sign in: that page then says why (DT-221).
        if (path == "/sign-in" && Http.Features.Get<DirectService.Transportation.Web.Pipeline.ExpiredForm>() is not null) q.Add("expired=1");
        return q.Count == 0 ? path : path + "?" + string.Join('&', q);
    }

    /// <summary>Server-side redirect after a successful POST (full page load, so the new cookies apply).</summary>
    protected void Go(string url) => Nav.NavigateTo(url, forceLoad: true);
}

/// <summary>Required-field checks with the field's label, in the page language (the services check again).</summary>
internal static class AccountValidation
{
    public static ApiProblem? Required(Loc l, params (string Field, string? Value, string LabelKey)[] fields)
    {
        var missing = fields.Where(f => string.IsNullOrWhiteSpace(f.Value))
            .Select(f => new ApiFieldError(f.Field, ErrorCodes.ValidationRequired, l.F("ui.requiredField", l[f.LabelKey]))).ToList();
        return missing.Count == 0 ? null : new ApiProblem { Status = 400, Code = ErrorCodes.ValidationFailed, Errors = missing };
    }
}
