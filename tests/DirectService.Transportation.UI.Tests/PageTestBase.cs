using System.Net;
using System.Text;
using System.Text.Json;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Screens rendered against a fake API: requests to <c>/api/v1/companies/c1/…</c> are answered from <see cref="Respond"/> by path (the
/// longest matching prefix wins); anything else answers 404 with a problem. A signed-in user with the given permissions is in company c1.
/// </summary>
public abstract class PageTestBase : UiTestBase
{
    protected const string Company = "c1";

    private readonly Dictionary<string, Func<HttpRequestMessage, object?>> _routes = new(StringComparer.Ordinal);

    /// <summary>Requests the screen sent (method and company-relative path with query).</summary>
    protected List<(string Method, string Path, string? Body)> Requests { get; } = [];

    /// <summary>
    /// When set, every answer arrives after this delay (a real network): a screen that starts a load without awaiting it never shows the
    /// answer, as in the browser.
    /// </summary>
    protected TimeSpan ResponseDelay { get; set; }

    protected PageTestBase(params string[] permissions)
    {
        Services.AddScoped(_ => new HttpClient(new FakeHandler(this)) { BaseAddress = new Uri("https://localhost/") });
        Services.AddScoped<Api>();
        Services.AddScoped<AppSession>();
        Services.AddScoped<FormConfig>();
        Services.AddScoped<TripLookups>();
        Services.AddScoped<Downloads>();
        Services.AddScoped<MoneyAccounts>();
        Services.AddScoped<AccountingLookups>();
        var perms = permissions.Length == 0 ? PermissionKeys.All.ToArray() : permissions;
        Shell.SetMe(new MeResponse("u1", "Test User", "user@example.test", "ar", "yyyy-MM-dd", Company, false, "Tenant", "Active",
            [new CompanyAccess(Company, "شركة الاختبار", "Test company", "Active", "Owner", true, perms)]));
        Shell.SelectCompany(Company);
        // Pick lists every screen may load.
        Respond("customers/options", new[] { new { id = "cu1", name = "شركة البوتاس العربية" } });
        Respond("materials/options", Array.Empty<object>());
        Respond("loading-sites/options", Array.Empty<object>());
        Respond("unloading-sites/options", Array.Empty<object>());
        Respond("partners/options", new[] { new { id = "p1", name = "النور للنقل" } });
        Respond("accounting/status", new { postingReady = true, missingPostingKeys = Array.Empty<string>(), openingDifferenceWarning = false, openingDifferenceBalance = 0, pendingPostings = 0 });
        Respond("accounting/money-accounts/options", new { cashBoxes = new[] { new { id = "cb1", name = "الصندوق الرئيسي" } }, bankAccounts = new[] { new { id = "b1", name = "البنك العربي" } } });
    }

    /// <summary>The visible text of a rendered screen (numbers inside Arabic text are isolated in their own elements).</summary>
    protected static string Text<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => string.Concat(cut.Nodes.Select(n => n.TextContent));

    /// <summary>
    /// Answers requests whose company-relative path (or, with a leading slash, API path after <c>/api/v1</c>) starts with
    /// <paramref name="path"/> with <paramref name="body"/> as JSON.
    /// </summary>
    protected void Respond(string path, object? body) => _routes[path] = _ => body;

    protected void Respond(string path, Func<HttpRequestMessage, object?> body) => _routes[path] = body;

    private readonly Dictionary<string, Dictionary<string, Func<HttpRequestMessage, object?>>> _companyRoutes = new(StringComparer.Ordinal);

    /// <summary>Requests to any company path: company token and company-relative path (company switch tests, DT-187).</summary>
    protected List<(string Company, string Path)> CompanyRequests { get; } = [];

    /// <summary>
    /// A delay for one company's answers only ("c1"), or for some of its paths ("c1/invoices"): an old company's answer that arrives after
    /// the switch.
    /// </summary>
    protected Dictionary<string, TimeSpan> CompanyDelay { get; } = new(StringComparer.Ordinal);

    /// <summary>Answers a path of another company than c1.</summary>
    protected void RespondFor(string company, string path, object? body)
    {
        if (!_companyRoutes.TryGetValue(company, out var routes)) _companyRoutes[company] = routes = new(StringComparer.Ordinal);
        routes[path] = _ => body;
    }

    /// <summary>Gives the user a second company (same permissions as c1) they can switch to.</summary>
    protected void AddCompany(string company, string name)
    {
        var me = Shell.Me!;
        Shell.SetMe(me with { Companies = [.. me.Companies, me.Companies[0] with { Id = company, Name = name, NameEn = name }] });
    }

    private sealed class FakeHandler(PageTestBase test) : HttpMessageHandler
    {
        private const string Prefix = "/api/v1/companies/" + Company + "/";
        private const string AnyCompany = "/api/v1/companies/";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (path.StartsWith("/api/v1/auth/antiforgery", StringComparison.Ordinal))
                return Json(new { headerName = "X-XSRF-TOKEN", token = "t" });
            // Company paths are relative to the company ("trips?…"); tenant, platform and other API paths keep a leading slash ("/tenant/…").
            // Another company's paths ("c2") are answered from its own routes (RespondFor) and recorded in CompanyRequests.
            string relative;
            string? company = null;
            if (path.StartsWith(Prefix, StringComparison.Ordinal)) (relative, company) = (path[Prefix.Length..], Company);
            else if (path.StartsWith(AnyCompany, StringComparison.Ordinal) && path.IndexOf('/', AnyCompany.Length) is var end and > 0)
                (relative, company) = (path[(end + 1)..], path[AnyCompany.Length..end]);
            else if (path.StartsWith("/api/v1/", StringComparison.Ordinal)) relative = path["/api/v1".Length..];
            else return NotFound();
            if (company is null || company == Company) test.Requests.Add((request.Method.Method, relative, body));
            if (company is not null) test.CompanyRequests.Add((company, relative));
            var delay = company is null ? test.ResponseDelay
                : test.CompanyDelay.Where(k => (company + "/" + relative).StartsWith(k.Key, StringComparison.Ordinal) && (k.Key == company || k.Key.StartsWith(company + "/", StringComparison.Ordinal)))
                    .Select(k => k.Value).DefaultIfEmpty(test.ResponseDelay).Max();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            var routes = company is null || company == Company ? test._routes
                : test._companyRoutes.TryGetValue(company, out var own) ? own : new Dictionary<string, Func<HttpRequestMessage, object?>>();
            var match = routes.Keys.Where(k => relative.StartsWith(k, StringComparison.Ordinal)).OrderByDescending(k => k.Length).FirstOrDefault();
            if (match is null) return NotFound();
            var value = routes[match](request);
            return value is HttpResponseMessage m ? m : Json(value);
        }

        private static HttpResponseMessage Json(object? value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, Api.Json), Encoding.UTF8, "application/json"),
        };

        private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"status\":404,\"code\":\"notFound\",\"detail\":\"Not found\"}", Encoding.UTF8, "application/problem+json"),
        };
    }
}
