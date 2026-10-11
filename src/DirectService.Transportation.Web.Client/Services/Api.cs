using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
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
    public string C(string path) => $"api/v1/companies/{Uri.EscapeDataString(shell.Company?.Id ?? throw new InvalidOperationException("No company selected."))}/{path.TrimStart('/')}";

    public Task<T> GetAsync<T>(string path, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Get, path, null, ct);

    public Task<T> PostAsync<T>(string path, object? body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Post, path, body, ct);

    public Task<T> PutAsync<T>(string path, object? body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Put, path, body, ct);

    public async Task PostAsync(string path, object? body, CancellationToken ct = default) => await SendAsync<object?>(HttpMethod.Post, path, body, ct).ConfigureAwait(false);

    public async Task PutAsync(string path, object? body, CancellationToken ct = default) => await SendAsync<object?>(HttpMethod.Put, path, body, ct).ConfigureAwait(false);

    public async Task DeleteAsync(string path, CancellationToken ct = default) => await SendAsync<object?>(HttpMethod.Delete, path, null, ct).ConfigureAwait(false);

    /// <summary>A file (export, PDF, print HTML) with its name and type; GET, or POST when a body is given.</summary>
    public async Task<ApiFile> FileAsync(string path, object? body = null, CancellationToken ct = default)
    {
        using var res = await RawAsync(body is null ? HttpMethod.Get : HttpMethod.Post, path, body, ct).ConfigureAwait(false);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
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
        using var res = await RawAsync(method, path, body, ct).ConfigureAwait(false);
        if (res.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Accepted || res.Content.Headers.ContentLength == 0) return default!;
        return (await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false))!;
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
        var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (res.IsSuccessStatusCode) return res;

        var problem = await ReadProblemAsync(res, ct).ConfigureAwait(false);
        res.Dispose();
        if (problem.Code == ErrorCodes.Antiforgery && !retried)
        {
            _xsrf = null;
            return await RawAsync(method, path, body, ct, retried: true).ConfigureAwait(false);
        }
        if (problem.Status == 401)
        {
            nav.NavigateTo("sign-in?returnUrl=" + Uri.EscapeDataString("/" + nav.ToBaseRelativePath(nav.Uri)), forceLoad: true);
        }
        throw new ApiException(problem);
    }

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
                if (p is not null) return p with { Status = p.Status == 0 ? (int)res.StatusCode : p.Status };
            }
        }
        catch (JsonException) { }
        return new ApiProblem { Status = (int)res.StatusCode, Code = "http." + (int)res.StatusCode };
    }
}

/// <summary>A refused or failed API call; <see cref="Problem"/> carries the message in the user's language.</summary>
public sealed class ApiException(ApiProblem problem) : Exception(problem?.Detail ?? problem?.Code)
{
    public ApiProblem Problem { get; } = problem ?? new ApiProblem();
}

public sealed record ApiFile(string Name, string ContentType, byte[] Bytes);
