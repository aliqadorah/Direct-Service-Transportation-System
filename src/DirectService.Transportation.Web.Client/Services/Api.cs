using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Platform.Contracts.Identity;
using DirectService.Transportation.Web.Client.Localization;
using Microsoft.AspNetCore.Components;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// The only door to the data (ADR-02, API-01): same-origin calls to <c>/api/v1</c> with the auth cookie, the antiforgery header on every
/// change (fetched from <c>GET /api/v1/auth/antiforgery</c> and refreshed once when the server rejects it), the user's language, and
/// problem details turned into <see cref="ApiException"/>. A 401 sends the user to sign-in and back. Company paths use the company of
/// the URL (<see cref="ShellState.Company"/>); the server checks tenant, company and permission on every request (SEC-05).
/// </summary>
public sealed class Api(HttpClient http, Loc loc, ShellState shell, NavigationManager nav)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private (string Header, string Token)? _xsrf;

    /// <summary><c>api/v1/companies/{company}/{path}</c> for the current company.</summary>
    public string C(string path) => C(shell.Company?.Id ?? throw new InvalidOperationException("No company selected."), path);

    /// <summary><c>api/v1/companies/{company}/{path}</c> for a company named on purpose (e.g. releasing a lock after a company switch).</summary>
    public static string C(string company, string path) => $"api/v1/companies/{Uri.EscapeDataString(company)}/{path.TrimStart('/')}";

    public Task<T> GetAsync<T>(string path, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Get, path, null, ct);

    public Task<T> PostAsync<T>(string path, object? body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Post, path, body, ct);

    public Task<T> PutAsync<T>(string path, object? body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Put, path, body, ct);

    public async Task PostAsync(string path, object? body, CancellationToken ct = default) => await SendAsync<object?>(HttpMethod.Post, path, body, ct).ConfigureAwait(false);

    public async Task PutAsync(string path, object? body, CancellationToken ct = default) => await SendAsync<object?>(HttpMethod.Put, path, body, ct).ConfigureAwait(false);

    public async Task DeleteAsync(string path, CancellationToken ct = default) => await SendAsync<object?>(HttpMethod.Delete, path, null, ct).ConfigureAwait(false);

    /// <summary>A file (export, PDF, print HTML) with its name and type; GET, or POST when a body is given.</summary>
    public async Task<ApiFile> FileAsync(string path, object? body = null, CancellationToken ct = default)
    {
        using var scope = Scope(body is null ? HttpMethod.Get : HttpMethod.Post, path, ct);
        using var res = await RawAsync(body is null ? HttpMethod.Get : HttpMethod.Post, path, body, scope.Token).ConfigureAwait(false);
        var bytes = await res.Content.ReadAsByteArrayAsync(scope.Token).ConfigureAwait(false);
        scope.EnsureCurrent();
        var name = res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "download";
        return new ApiFile(name, res.Content.Headers.ContentType?.ToString() ?? "application/octet-stream", bytes);
    }

    /// <summary>Uploads a file as multipart form data (imports: <c>file</c> and an optional <c>mapping</c>).</summary>
    public async Task<T> UploadAsync<T>(string path, Stream file, string fileName, IReadOnlyDictionary<string, string>? fields = null, HttpMethod? method = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct).ConfigureAwait(false);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(ms.ToArray()), "file", fileName } };
        foreach (var (k, v) in fields ?? new Dictionary<string, string>()) form.Add(new StringContent(v), k);
        return await SendAsync<T>(method ?? HttpMethod.Post, path, form, ct).ConfigureAwait(false);
    }

    private static async Task<HttpContent> Clone(MultipartFormDataContent source)
    {
        var copy = new MultipartFormDataContent();
        foreach (var part in source)
        {
            var bytes = await part.ReadAsByteArrayAsync().ConfigureAwait(false);
            var c = new ByteArrayContent(bytes);
            foreach (var h in part.Headers) c.Headers.TryAddWithoutValidation(h.Key, h.Value);
            copy.Add(c);
        }
        return copy;
    }

    /// <summary>Forgets the antiforgery token (after sign-in or sign-out the server issues a new one).</summary>
    public void ResetAntiforgery() => _xsrf = null;

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var scope = Scope(method, path, ct);
        using var res = await RawAsync(method, path, body, scope.Token).ConfigureAwait(false);
        var value = res.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Accepted || res.Content.Headers.ContentLength == 0
            ? default! : (await res.Content.ReadFromJsonAsync<T>(Json, scope.Token).ConfigureAwait(false))!;
        scope.EnsureCurrent();
        return value;
    }

    /// <summary>
    /// A read of the company on screen belongs to it (DT-187): it is cancelled when the user leaves the company
    /// (<see cref="ShellState.CompanyToken"/>), and an answer that arrives after a switch is dropped with an
    /// <see cref="OperationCanceledException"/> (which Blazor ignores in lifecycle methods and event handlers), so no page or shared state
    /// ever shows another company's data. A change the user started is let finish and answers the component that sent it (a save, a lock
    /// it took and must give back). A request for another company named on purpose (the tenant's data export, a lock released after the
    /// switch) and tenant or platform paths are not scoped.
    /// </summary>
    private RequestScope Scope(HttpMethod method, string path, CancellationToken ct)
    {
        var company = CompanyOf(path);
        if (company is null || company != shell.Company?.Id || (method != HttpMethod.Get && method != HttpMethod.Head))
            return new RequestScope(null, null, null, ct);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, shell.CompanyToken);
        return new RequestScope(shell, company, linked, linked.Token);
    }

    /// <summary>The company token of an <c>api/v1/companies/{company}/…</c> path, else null.</summary>
    public static string? CompanyOf(string path)
    {
        const string prefix = "api/v1/companies/";
        var p = (path ?? "").TrimStart('/');
        if (!p.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var end = p.IndexOfAny(['/', '?'], prefix.Length);
        return Uri.UnescapeDataString(end < 0 ? p[prefix.Length..] : p[prefix.Length..end]);
    }

    private sealed class RequestScope(ShellState? shell, string? company, CancellationTokenSource? linked, CancellationToken token) : IDisposable
    {
        public CancellationToken Token => token;

        /// <summary>Drops the answer when the company changed while it was on its way.</summary>
        public void EnsureCurrent()
        {
            if (shell is not null && shell.Company?.Id != company) throw new OperationCanceledException("The company changed; the answer is discarded.");
            token.ThrowIfCancellationRequested();
        }

        public void Dispose() => linked?.Dispose();
    }

    private async Task<HttpResponseMessage> RawAsync(HttpMethod method, string path, object? body, CancellationToken ct, bool retried = false)
    {
        var change = method != HttpMethod.Get && method != HttpMethod.Head;
        if (change && _xsrf is null) await LoadAntiforgeryAsync(ct).ConfigureAwait(false);
        using var req = new HttpRequestMessage(method, path);
        req.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(loc.Language));
        if (change && _xsrf is { } x) req.Headers.TryAddWithoutValidation(x.Header, x.Token);
        // A retry after an antiforgery refresh rebuilds JSON bodies; uploads are buffered so they can be sent again.
        if (body is HttpContent content) req.Content = content is MultipartFormDataContent m ? await Clone(m).ConfigureAwait(false) : content;
        else if (body is not null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // No answer at all (network down, server stopped): the only case that is a lost connection (DT-209).
            throw Failed(new ApiProblem { Status = 0, Code = NetworkCode });
        }
        if (res.IsSuccessStatusCode) return res;

        var problem = await ReadProblemAsync(res, ct).ConfigureAwait(false);
        res.Dispose();
        if (problem.Code == ErrorCodes.Antiforgery && !retried)
        {
            _xsrf = null;
            return await RawAsync(method, path, body, ct, retried: true).ConfigureAwait(false);
        }
        // Refused again with a fresh token (DT-221): the page is out of date (signed in or out elsewhere); the shell offers a reload.
        if (problem.Code == ErrorCodes.Antiforgery) shell.MarkPageExpired();
        if (problem.Status == 401)
        {
            nav.NavigateTo("sign-in?returnUrl=" + Uri.EscapeDataString("/" + nav.ToBaseRelativePath(nav.Uri)), forceLoad: true);
        }
        // A staff member's support window closed (DT-220): the shell leaves the company and says why, whatever page asked.
        if (problem.Status == 403 && IsSupportEnd(problem.Code) && shell.Me?.IsPlatformStaff == true)
            shell.EndSupport(new ShellState.SupportEnd(problem.Code!, problem.EndedAt, problem.EndedBy));
        throw Failed(problem);
    }

    /// <summary>The reasons the server gives when a support window is no longer open.</summary>
    public static bool IsSupportEnd(string? code) =>
        code is ErrorCodes.SupportEndedBySubscriber or ErrorCodes.SupportWindowExpired or ErrorCodes.SupportNotApproved;

    /// <summary>The code of a call that got no answer (status 0).</summary>
    public const string NetworkCode = "network";

    private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);
    private ApiProblem? _lastFailure;

    /// <summary>Remembers the status of a failed call by its reference, so an error screen can tell what happened (DT-209).</summary>
    private ApiException Failed(ApiProblem problem)
    {
        _lastFailure = problem;
        if (problem.Reference is { } r)
        {
            if (_failures.Count > 100) _failures.Clear();
            _failures[r] = problem.Status;
        }
        return new ApiException(problem);
    }

    /// <summary>
    /// The HTTP status of the failed call with this reference (0 = no answer); without a reference, the last failure's status; null when
    /// nothing failed.
    /// </summary>
    public int? FailureStatus(string? reference) =>
        reference is not null && _failures.TryGetValue(reference, out var status) ? status : _lastFailure?.Status;

    private async Task LoadAntiforgeryAsync(CancellationToken ct)
    {
        var t = await http.GetFromJsonAsync<AntiforgeryResponse>("api/v1/auth/antiforgery", Json, ct).ConfigureAwait(false);
        if (t is not null) _xsrf = (t.HeaderName, t.Token);
    }

    private static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            if (res.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
            {
                var p = await res.Content.ReadFromJsonAsync<ApiProblem>(Json, ct).ConfigureAwait(false);
                if (p is not null) return Specific(p with { Status = p.Status == 0 ? (int)res.StatusCode : p.Status });
            }
        }
        catch (JsonException) { }
        return new ApiProblem { Status = (int)res.StatusCode, Code = "http." + (int)res.StatusCode };
    }

    /// <summary>
    /// A validation answer's own detail only says "correct the highlighted fields"; a screen that shows the detail in a banner or toast
    /// (no field to highlight) must show the field messages themselves (owner feedback 18 Oct 2026, DT-208). Forms with a
    /// ProblemValidator still place each message on its field from <see cref="ApiProblem.Errors"/>.
    /// </summary>
    internal static ApiProblem Specific(ApiProblem p) =>
        p.Errors is { Count: > 0 } errors && errors.Select(e => e.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.Ordinal).ToList() is { Count: > 0 } messages
            ? p with { Detail = string.Join(" ", messages) }
            : p;
}

/// <summary>A refused or failed API call; <see cref="Problem"/> carries the message in the user's language.</summary>
public sealed class ApiException(ApiProblem problem) : Exception(problem?.Detail ?? problem?.Code)
{
    public ApiProblem Problem { get; } = problem ?? new ApiProblem();
}

public sealed record ApiFile(string Name, string ContentType, byte[] Bytes);
