using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Configuration;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Transportation.Web.Workers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Acc = DirectService.Transportation.Domain.Acc;
using Cfg = DirectService.Transportation.Domain.Cfg;
using Core = DirectService.Transportation.Domain.Core;
using Trn = DirectService.Transportation.Domain.Trn;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Cross-tenant sweep (DT-218, after SA-1): two tenants WITH THE SAME NAME, two companies each (their own and the demo company) and a record
/// of every kind a route can name. For EVERY endpoint of the route table: tenant A's reads (lists, details, reports, exports, search,
/// dashboard, notifications, its own profile and tenant pages) never contain a token or a marker of tenant B — also when a filter of the
/// query string names B's record — and every route that names a record answers 404 to A for B's record (A cannot read or act on it).
/// Provider-console routes answer 404 to A. Each route is classified from the route table; a route or parameter the sweep cannot classify
/// fails the test, so new endpoints are covered automatically.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TenantIsolationSweepTests(ApiFactory f)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);
    private static readonly string Month = Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private static readonly Regex TokenPattern = new("(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{22}(?![A-Za-z0-9_-])", RegexOptions.Compiled);

    /// <summary>Route values that are not records, with the values a read is tried with.</summary>
    private static readonly Dictionary<string, string[]> Values = new(StringComparer.Ordinal)
    {
        ["month"] = [Month], ["kind"] = ["receivables", "payables"], ["viewKey"] = ["TRN.Trips"], ["entityType"] = ["TRN.Customer", "TRN.Trip", "TRN.Driver"],
        ["layoutType"] = ["claim", "receipt"], ["key"] = [],   // the standard report catalog, read at run time
        // Value parameters of writes only (never called with values by this sweep: they act on the caller's own company).
        ["action"] = [], ["step"] = [], ["postingKey"] = [],
    };

    /// <summary>The entity a route or query parameter names (its URL token type); null = not a record.</summary>
    internal static Type? EntityOf(string param, string route) => param switch
    {
        "companyId" => typeof(Core.Company),
        "account" => typeof(Acc.Account), "bankAccount" => typeof(Acc.BankAccount), "budget" => typeof(Acc.Budget), "cashBox" => typeof(Acc.CashBox),
        "costCentre" => typeof(Acc.CostCentre), "draft" => typeof(Acc.JournalDraft), "entry" => typeof(Acc.JournalEntry), "template" => typeof(Acc.JournalTemplate),
        "document" => typeof(Acc.ApArDocument), "party" or "supplier" => typeof(Acc.ApArParty), "return" => typeof(Acc.ApArReturn),
        "pending" => typeof(Acc.PendingPosting),
        "submission" => route.Contains("/apar/", StringComparison.Ordinal) ? typeof(Acc.ApArEInvoiceSubmission) : typeof(Trn.EInvoiceSubmission),
        "payment" => route.Contains("/apar/", StringComparison.Ordinal) ? typeof(Acc.ApArPayment) : typeof(Trn.PartnerPayment),
        "advance" => typeof(Trn.Advance), "creditNote" => typeof(Trn.CreditNote), "customer" => typeof(Trn.Customer), "driver" => typeof(Trn.Driver),
        "invoice" => route.Contains("/subscription/", StringComparison.Ordinal) ? typeof(Core.SubscriptionInvoice) : typeof(Trn.Invoice),
        "site" => route.Contains("/unloading-sites/", StringComparison.Ordinal) ? typeof(Trn.UnloadingSite) : typeof(Trn.LoadingSite),
        "loadingSite" => typeof(Trn.LoadingSite), "unloadingSite" => typeof(Trn.UnloadingSite), "material" => typeof(Trn.Material),
        "ownershipType" => typeof(Trn.OwnershipType), "partner" => typeof(Trn.Partner), "receipt" => typeof(Trn.Receipt), "settlement" => typeof(Trn.Settlement),
        "tariff" => typeof(Trn.Tariff), "trip" => typeof(Trn.Trip),
        "field" => typeof(Cfg.CustomFieldDefinition), "pin" => typeof(Cfg.DashboardPin), "lockId" => typeof(Cfg.RecordLock), "report" => typeof(Cfg.SavedReport),
        "sequence" => typeof(Cfg.NumberSequence),
        "invitation" => typeof(Core.Invitation), "notification" => typeof(Core.Notification), "role" => typeof(Core.Role), "user" => typeof(Core.User),
        "session" => typeof(Core.UserSession), "grant" => typeof(Core.SupportAccessGrant),
        _ => null,
    };

    /// <summary>Background export files are named by a random id under the requester's own tenant/company/user folder (not a URL token).</summary>
    private sealed class ExportId;

    private sealed record Seeded(SignedInUser Owner, long TenantId, IReadOnlyList<string> Companies, string ExportId);

    private sealed record Row(string Token, long? CompanyId);

    [Fact]
    public async Task Tenant_A_never_sees_or_acts_on_tenant_B_records_on_any_endpoint()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var name = "Twin Transport " + tag;
        var staff = await PlatformTests.StaffAsync(f);
        var plan = await JsonAsync(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", new
        {
            code = "SW" + tag[..6].ToUpperInvariant(), nameAr = "خطة", nameEn = "Sweep plan", price = 50m, billingCycle = "Monthly", maxCompanies = 5, maxActiveUsers = 25,
            maxTripsPerMonth = 20000, maxStorageMb = 10240, isActive = true, rowVersion = (string?)null,
        }));
        var a = await SeedAsync(await f.SignUpOwnerAsync(name), "OWNA" + tag, staff, plan.GetProperty("id").GetString()!);
        var b = await SeedAsync(await f.SignUpOwnerAsync(name), "LEAKB" + tag, staff, plan.GetProperty("id").GetString()!);
        Assert.Equal(2, a.Companies.Count);
        Assert.Equal(2, b.Companies.Count);

        var mine = await IndexAsync(a.TenantId);
        var theirs = await IndexAsync(b.TenantId);
        var tokenTypes = theirs.ToDictionary(kv => kv.Key, kv => kv.Value);
        var foreignTokens = theirs.Values.SelectMany(v => v).Select(r => r.Token).ToHashSet(StringComparer.Ordinal);
        foreignTokens.Add(Token(typeof(Core.Tenant), await f.DbAsync(db => db.Set<Core.Tenant>().IgnoreQueryFilters().Where(t => t.Id == b.TenantId).Select(t => t.PublicId).SingleAsync())));
        string[] markers = ["LEAKB" + tag, b.Owner.Email];
        var companyIds = await f.DbAsync(db => db.Set<Core.Company>().IgnoreQueryFilters().Where(c => c.TenantId == a.TenantId)
            .Select(c => new { c.Id, c.PublicId }).ToListAsync());
        var companyOf = companyIds.ToDictionary(c => Token(typeof(Core.Company), c.PublicId), c => c.Id, StringComparer.Ordinal);
        Values["key"] = (await JsonAsync(await a.Owner.Client.GetAsync($"/api/v1/companies/{a.Companies[0]}/reports/catalog")))
            .EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToArray();

        var problems = new List<string>();
        var classes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var ownOk = new HashSet<string>(StringComparer.Ordinal);       // "param:Type" read successfully with A's own record at least once
        var ownTried = new Dictionary<string, string>(StringComparer.Ordinal);
        int reads = 0, scanned = 0, foreignCalls = 0, foreignQuery = 0, binary = 0;

        foreach (var e in f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
                     .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1", StringComparison.Ordinal) == true))
        {
            var route = e.RoutePattern.RawText!;
            foreach (var method in e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                var id = $"{method} {route}";
                // Not tenant data: anonymous account flows and the signed payment webhook.
                if (route.StartsWith("/api/v1/auth/", StringComparison.Ordinal) || route == "/api/v1/billing/webhook") { Count(classes, "anonymous account / webhook"); continue; }
                // Provider console: never open to a tenant user.
                if (route.StartsWith("/api/v1/platform/", StringComparison.Ordinal))
                {
                    Count(classes, "provider console (404 to tenants)");
                    var r = await SendAsync(a.Owner.Client, method, Fill(route, a.Companies[0], _ => "AAAAAAAAAAAAAAAAAAAAAA"));
                    if (r.Status != HttpStatusCode.NotFound) problems.Add($"{id}: a tenant user got {(int)r.Status} on a provider-console route");
                    continue;
                }
                if (!(route.StartsWith("/api/v1/companies/{company}", StringComparison.Ordinal) || route.StartsWith("/api/v1/tenant", StringComparison.Ordinal)
                      || route.StartsWith("/api/v1/me", StringComparison.Ordinal) || route == "/api/v1/announcements"))
                {
                    problems.Add($"{id}: not classified (unknown route family)");
                    continue;
                }
                var parameters = e.RoutePattern.Parameters.Select(p => p.Name).Where(p => p != "company").ToList();
                var unknown = parameters.Where(p => EntityOf(p, route) is null && p != "export" && !Values.ContainsKey(p)).ToList();
                if (unknown.Count > 0) { problems.Add($"{id}: route parameter(s) {string.Join(", ", unknown)} not classified"); continue; }
                var records = parameters.Where(p => EntityOf(p, route) is not null || p == "export").ToList();
                var isRead = method == "GET";

                // 1. A names B's record: 404 (cannot read or act on it).
                if (records.Count > 0)
                {
                    Count(classes, isRead ? "record read (B's record → 404; own record scanned)" : "record action (B's record → 404)");
                    string? Foreign(string p) => p == "export" ? b.ExportId : tokenTypes.GetValueOrDefault(EntityOf(p, route)!)?.FirstOrDefault()?.Token;
                    var missingSample = records.Where(p => Foreign(p) is null).ToList();
                    if (missingSample.Count > 0) { problems.Add($"{id}: no record of tenant B to try for {string.Join(", ", missingSample)} (seed it)"); }
                    else
                    {
                        foreignCalls++;
                        var url = Fill(route, a.Companies[0], p => records.Contains(p) ? Foreign(p)! : Values[p].FirstOrDefault() ?? "x");
                        var r = await SendAsync(a.Owner.Client, method, url, Multipart(e));
                        if (!Refused(r) && !(await ReleaseLeftBsLockAsync(route, method, r, b.TenantId))) problems.Add($"{id}: tenant A naming tenant B's record got {(int)r.Status} {r.Code} (expected 404)");
                        Scan(r, foreignTokens, markers, id + " [B's record]", problems, ref binary);
                    }
                }
                else Count(classes, isRead ? (route.StartsWith("/api/v1/companies/", StringComparison.Ordinal) ? "company read (scanned)" : "tenant / profile read (scanned)")
                    : "own write without a record (acts on A's own company or profile only)");

                if (!isRead) continue;
                // 2. A's own reads (both companies): never contain B's tokens or markers, also when a query filter names B's record.
                var query = QueryParameters(e);
                foreach (var company in route.StartsWith("/api/v1/companies/", StringComparison.Ordinal) ? a.Companies : a.Companies.Take(1))
                {
                    var companyId = companyOf[company];
                    string? Own(string p, string r) => p == "export" ? a.ExportId
                        : mine.GetValueOrDefault(EntityOf(p, r)!)?.Where(x => x.CompanyId is null || x.CompanyId == companyId || !r.StartsWith("/api/v1/companies/", StringComparison.Ordinal))
                            .Select(x => x.Token).FirstOrDefault();
                    if (records.Any(p => Own(p, route) is null)) continue;   // A has no record of that kind in this company
                    var variants = records.Count == 0 && parameters.Count > 0 ? parameters.SelectMany(p => Values[p]).Distinct().Select(v => (string?)v).ToList() : [null];
                    foreach (var value in variants)
                    {
                        var path = Fill(route, company, p => records.Contains(p) ? Own(p, route)! : value ?? Values[p].FirstOrDefault() ?? "x");
                        var qs = string.Join('&', query.Select(q => q.Name + "=" + Uri.EscapeDataString(QueryValue(q, route, x => Own(x, route)))).Where(s => !s.EndsWith('=')));
                        reads++;
                        var r = await SendAsync(a.Owner.Client, method, path + (qs.Length > 0 ? "?" + qs : ""));
                        if ((int)r.Status >= 500) problems.Add($"{id}: own read failed with {(int)r.Status} ({path})");
                        foreach (var p in records.Where(p => p != "export"))
                        {
                            var key = p + ":" + EntityOf(p, route)!.Name;
                            ownTried.TryAdd(key, id);
                            if (r.Status == HttpStatusCode.OK) ownOk.Add(key);
                        }
                        if (Scan(r, foreignTokens, markers, id, problems, ref binary)) scanned++;
                        // The same read with a query filter naming B's record (one filter at a time).
                        foreach (var q in query.Where(q => EntityOf(q.Name, route) is not null))
                        {
                            var foreign = tokenTypes.GetValueOrDefault(EntityOf(q.Name, route)!)?.FirstOrDefault()?.Token;
                            if (foreign is null) { problems.Add($"{id}: no record of tenant B to try for the query filter {q.Name} (seed it)"); continue; }
                            var fq = string.Join('&', query.Select(x => x.Name + "=" + Uri.EscapeDataString(x.Name == q.Name ? foreign : QueryValue(x, route, y => Own(y, route))))
                                .Where(s => !s.EndsWith('=')));
                            foreignQuery++;
                            var rf = await SendAsync(a.Owner.Client, method, path + "?" + fq);
                            if ((int)rf.Status >= 500) problems.Add($"{id}: filter {q.Name} = B's record failed with {(int)rf.Status}");
                            Scan(rf, foreignTokens, markers, $"{id} [?{q.Name}=B's record]", problems, ref binary);
                        }
                    }
                }
            }
        }

        // A parameter mapped to the wrong entity would make every "404" above meaningless: each mapping must open A's own record somewhere.
        foreach (var (key, where) in ownTried.Where(t => !ownOk.Contains(t.Key)))
            problems.Add($"{where}: {key} never opened tenant A's own record (wrong entity mapping?)");

        var report = $"Sweep: {classes.Values.Sum()} operations ({string.Join("; ", classes.Select(c => $"{c.Key}: {c.Value}"))}); own reads {reads} " +
            $"(scanned {scanned}, binary not scanned {binary}), B's record named {foreignCalls}, query filters with B's record {foreignQuery}.";
        Console.WriteLine(report);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "tenant-isolation-sweep.txt"), report + "\n" + string.Join('\n', problems));
        Assert.True(problems.Count == 0, report + "\n" + string.Join('\n', problems));
    }

    private static void Count(SortedDictionary<string, int> classes, string name) => classes[name] = classes.TryGetValue(name, out var n) ? n + 1 : 1;

    /// <summary>A refusal that reveals nothing: 404, or a validation answer given before the record was looked up (nothing was read).</summary>
    private static bool Refused(Response r) =>
        r.Status == HttpStatusCode.NotFound || (r.Status == HttpStatusCode.BadRequest && (r.Code is null || r.Code.StartsWith("validation", StringComparison.Ordinal)
            || r.Code == "badRequest"));

    private static bool Multipart(RouteEndpoint e) =>
        e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes.Any(c => c.StartsWith("multipart/", StringComparison.Ordinal)) == true;

    /// <summary>
    /// Releasing a record lock is idempotent: a lock the caller cannot see (gone, or another tenant's) answers 204 and nothing happens. That is
    /// accepted only when tenant B's locks are all still there afterwards.
    /// </summary>
    private async Task<bool> ReleaseLeftBsLockAsync(string route, string method, Response r, long tenantB) =>
        method == "DELETE" && route.EndsWith("/locks/{lockId}", StringComparison.Ordinal) && r.Status == HttpStatusCode.NoContent
        && await f.DbAsync(db => db.Set<Cfg.RecordLock>().IgnoreQueryFilters().AnyAsync(l => l.TenantId == tenantB && !l.IsDeleted));

    private sealed record Response(HttpStatusCode Status, string? Code, string ContentType, byte[] Body);

    private static async Task<Response> SendAsync(HttpClient client, string method, string url, bool multipart = false)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (method != "GET" && method != "DELETE")
            req.Content = multipart ? new MultipartFormDataContent { { new ByteArrayContent([0x50, 0x4B, 0x03, 0x04]), "file", "import.xlsx" } } : JsonContent.Create(new { });
        using var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsByteArrayAsync();
        string? code = null;
        if (res.Content.Headers.ContentType?.MediaType == "application/problem+json")
        {
            try { code = JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var c) ? c.GetString() : null; }
            catch (JsonException) { }
        }
        return new Response(res.StatusCode, code, res.Content.Headers.ContentType?.MediaType ?? "", body);
    }

    /// <summary>Looks for B's tokens and markers in a response (text, or the parts of a zip such as xlsx); false when it could not be read.</summary>
    private static bool Scan(Response r, HashSet<string> foreign, string[] markers, string where, List<string> problems, ref int binary)
    {
        var text = Text(r);
        if (text is null) { binary++; return false; }
        var leaked = TokenPattern.Matches(text).Select(m => m.Value).Where(foreign.Contains).Distinct().Take(3).ToList();
        leaked.AddRange(markers.Where(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)));
        if (leaked.Count > 0) problems.Add($"LEAK {where}: tenant B's data in tenant A's answer ({(int)r.Status}): {string.Join(", ", leaked)}");
        return true;
    }

    private static string? Text(Response r)
    {
        if (r.Body.Length > 1 && r.Body[0] == (byte)'P' && r.Body[1] == (byte)'K')
        {
            using var zip = new ZipArchive(new MemoryStream(r.Body));
            var sb = new StringBuilder();
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                sb.Append(reader.ReadToEnd());
            }
            return sb.ToString();
        }
        if (r.ContentType.Contains("pdf", StringComparison.Ordinal) || r.ContentType.StartsWith("image/", StringComparison.Ordinal)) return null;
        return Encoding.UTF8.GetString(r.Body);
    }

    private static string Fill(string route, string company, Func<string, string> value) =>
        Regex.Replace(route.Replace("{company}", company, StringComparison.Ordinal), @"\{([^}:?]+)[^}]*\}", m => Uri.EscapeDataString(value(m.Groups[1].Value)));

    private sealed record QueryParam(string Name, Type Type);

    /// <summary>The query-string parameters of an endpoint's handler (simple parameters not in the route, and the properties of [AsParameters] types).</summary>
    private static List<QueryParam> QueryParameters(RouteEndpoint e)
    {
        var handler = e.Metadata.GetMetadata<MethodInfo>();
        if (handler is null) return [];
        var routeNames = e.RoutePattern.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<QueryParam>();
        foreach (var p in handler.GetParameters())
        {
            if (p.GetCustomAttribute<AsParametersAttribute>() is not null)
            {
                list.AddRange(p.ParameterType.GetConstructors()[0].GetParameters().Where(x => Simple(x.ParameterType))
                    .Select(x => new QueryParam(char.ToLowerInvariant(x.Name![0]) + x.Name[1..], x.ParameterType)));
                continue;
            }
            if (p.Name is not null && !routeNames.Contains(p.Name) && Simple(p.ParameterType)) list.Add(new QueryParam(p.Name, p.ParameterType));
        }
        return list;
    }

    private static bool Simple(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u == typeof(string) || u == typeof(DateOnly) || u == typeof(int) || u == typeof(bool) || u == typeof(decimal) || u == typeof(long);
    }

    private static string QueryValue(QueryParam q, string route, Func<string, string?> own)
    {
        var u = Nullable.GetUnderlyingType(q.Type) ?? q.Type;
        if (EntityOf(q.Name, route) is not null) return own(q.Name) ?? "";
        if (u == typeof(DateOnly)) return q.Name.Contains("from", StringComparison.OrdinalIgnoreCase) ? "2000-01-01" : "2100-12-31";
        if (u == typeof(int)) return q.Name.Contains("size", StringComparison.OrdinalIgnoreCase) ? "200" : q.Name == "year" ? Today.Year.ToString(CultureInfo.InvariantCulture) : "";
        if (u == typeof(string)) return q.Name switch { "month" => Month, "periodFrom" or "periodTo" => Month, "format" => route.EndsWith("/print", StringComparison.Ordinal) ? "html" : "csv", _ => "" };
        return "";
    }

    // ------------------------------------------------------------------ tokens of every record of a tenant

    private string Token(Type type, Guid publicId) => f.Services.GetRequiredService<IUrlTokenService>().Encode(type, publicId);

    private async Task<Dictionary<Type, List<Row>>> IndexAsync(long tenantId)
    {
        var index = new Dictionary<Type, List<Row>>();
        var types = await f.DbAsync(db => Task.FromResult(db.Model.GetEntityTypes().Select(t => t.ClrType)
            .Where(t => t.GetProperty("PublicId")?.PropertyType == typeof(Guid) && t.GetProperty("TenantId") is not null).ToList()));
        foreach (var type in types)
        {
            var load = typeof(TenantIsolationSweepTests).GetMethod(nameof(RowsAsync), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type);
            var rows = await f.DbAsync(db => (Task<List<(Guid, long?)>>)load.Invoke(null, [db, tenantId])!);
            if (rows.Count > 0) index[type] = rows.Select(r => new Row(Token(type, r.Item1), r.Item2)).ToList();
        }
        return index;
    }

    private static async Task<List<(Guid, long?)>> RowsAsync<T>(MasterDbContext db, long tenantId) where T : class
    {
        var q = db.Set<T>().IgnoreQueryFilters().AsNoTracking();
        q = typeof(T).GetProperty("TenantId")!.PropertyType == typeof(long)
            ? q.Where(e => EF.Property<long>(e, "TenantId") == tenantId)
            : q.Where(e => EF.Property<long?>(e, "TenantId") == tenantId);
        var company = typeof(T).GetProperty("CompanyId")?.PropertyType;
        if (company == typeof(long))
            return (await q.Select(e => new { P = EF.Property<Guid>(e, "PublicId"), C = EF.Property<long>(e, "CompanyId") }).ToListAsync()).Select(x => (x.P, (long?)x.C)).ToList();
        if (company == typeof(long?))
            return (await q.Select(e => new { P = EF.Property<Guid>(e, "PublicId"), C = EF.Property<long?>(e, "CompanyId") }).ToListAsync()).Select(x => (x.P, x.C)).ToList();
        return (await q.Select(e => EF.Property<Guid>(e, "PublicId")).ToListAsync()).Select(p => (p, (long?)null)).ToList();
    }

    // ------------------------------------------------------------------ seeding: a record of every kind a route can name

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<Seeded> SeedAsync(SignedInUser o, string marker, SignedInUser staff, string planId)
    {
        var c = o.Client;
        string U(string p) => $"/api/v1/companies/{o.CompanyId}{p}";
        await (await c.PostAsJsonAsync(U("/accounting/accounts/template"), new { })).EnsureOkAsync();
        var acc = (await c.GetFromJsonAsync<List<AccountResponse>>(U("/accounting/accounts")))!.ToDictionary(x => x.Code);
        var einv = await ReadAsync<EInvoiceSettingsResponse>(await c.GetAsync(U("/settings/e-invoicing")));
        await (await c.PutAsJsonAsync(U("/settings/e-invoicing"), new EInvoiceSettingsRequest(true, "Simulator", "12345678", "Sales", "4569874", "client-77", "s3cret-77", "Test",
            einv.RowVersion))).EnsureOkAsync();
        var box = (await c.GetFromJsonAsync<List<CashBoxResponse>>(U("/accounting/cash-boxes")))!.First();
        await (await c.PostAsJsonAsync(U("/accounting/bank-accounts"), new BankAccountRequest(marker + " current", "Cairo Amman Bank", "0131000302", null, null, acc["110201"].Id, null)))
            .EnsureOkAsync();
        var cc = await ReadAsync<CostCentreResponse>(await c.PostAsJsonAsync(U("/accounting/cost-centres"), new CostCentreRequest("CC-01", marker + " fleet", null)));
        var customer = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U("/customers"),
            new CustomerRequest(marker + " Customer", "200014587", null, "0781234567", null, null, "Aqaba", null, 30, null, null, null, null, null)));
        var partner = await ReadAsync<PartnerResponse>(await c.PostAsJsonAsync(U("/partners"), new PartnerRequest(marker + " Partner", null, null, null, null, null, null, null, null, null, null)));
        var material = await ReadAsync<MaterialResponse>(await c.PostAsJsonAsync(U("/materials"), new MaterialRequest(marker + " Potash", null, null, null)));
        var loading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U("/loading-sites"), new SiteRequest(marker + " Ghor", null, null, null)));
        var unloading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U("/unloading-sites"), new SiteRequest(marker + " Port", null, null, null)));
        var types = (await c.GetFromJsonAsync<List<OwnershipTypeResponse>>(U("/ownership-types")))!;
        var rented = await ReadAsync<OwnershipTypeResponse>(await c.PostAsJsonAsync(U("/ownership-types"), new OwnershipTypeRequest(marker + " Rented", "External", null, null)));
        DriverRequest Driver(string n, string nid, string plate, string type, string? partnerId) =>
            new(n, nid, "0791234567", null, null, "L-1", "C", Today.AddYears(1), "Cash", null, null, null, plate, "Volvo", null, 32m, type, partnerId,
                Today.AddYears(1), Today.AddYears(1), Today.AddYears(1), null, null, null, null);
        var own = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U("/drivers"), Driver(marker + " Ahmad", "9871234560", "10-11111", types.Single(t => t.IsBuiltIn).Id, null)));
        await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U("/drivers"), Driver(marker + " Sami", "9871234561", "10-22222", rented.Id, partner.Id)));
        await (await c.PostAsJsonAsync(U("/tariffs"), new TariffRequest("Customer", customer.Id, material.Id, null, null, 6.000m, null, null, null, null, null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U("/tariffs"), new TariffRequest("Partner", partner.Id, null, null, null, 2.750m, null, null, null, null, null))).EnsureOkAsync();
        await (await c.PutAsJsonAsync(U($"/monthly-rates/{Month}"), new MonthRatesRequest([new SiteRateRequest(loading.Id, 3.500m, 4.250m, null)]))).EnsureOkAsync();
        var drivers = (await c.GetFromJsonAsync<Contracts.Common.PagedResult<DriverResponse>>(U("/drivers?pageSize=50")))!.Items;
        var ext = drivers.Single(d => d.FullName.EndsWith("Sami", StringComparison.Ordinal));
        async Task<TripResponse> Trip(string receipt, DriverResponse driver, string? partnerId) =>
            (await ReadAsync<TripSaveResult>(await c.PostAsJsonAsync(U("/trips"), new TripRequest(receipt, Today, null, null, driver.Id, customer.Id, loading.Id, unloading.Id,
                material.Id, partnerId, 32.000m, 31.850m, null, null, null, null, null, null, null)))).Trip;
        var t1 = await Trip("R-1", own, null);
        var t2 = await Trip("R-2", ext, partner.Id);
        await Trip("R-3", own, null);

        // Invoicing and cash: an approved tax invoice (e-invoice submission), a credit note, a receipt, an advance, a settlement, a partner payment.
        var inv = await ReadAsync<InvoiceDetails>(await c.PostAsJsonAsync(U("/invoices"), new InvoiceRequest(customer.Id, "Manual", Today, null, null,
            [new ManualLineRequest(marker + " move", 2, 350m)], 0m, 0m, null, "TaxInvoice", null, true, null, null)));
        await (await c.PostAsJsonAsync(U("/credit-notes"), new CreditNoteRequest(inv.Invoice.Id, Today, marker + " damaged", [new CreditLineRequest(inv.Lines[0].Id, 1)], null)))
            .EnsureOkAsync();
        await (await c.PostAsJsonAsync(U("/receipts"), new ReceiptRequest(customer.Id, Today, null, 75m, "Cash", box.Id, null, null, 0, null, null))).EnsureOkAsync();
        var adv = await JsonAsync(await c.PostAsJsonAsync(U("/advances"), new AdvanceRequest(own.Id, Today, 5m, "Cash", box.Id, null, null, null, null, null)));
        await (await c.PostAsJsonAsync(U("/settlements"), new SettlementRequest(own.Id, Today, [t1.Id],
            [adv.GetProperty("advance").GetProperty("id").GetString()!], null, "Cash", box.Id, null, null, null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U("/partner-payments"), new PartnerPaymentRequest(partner.Id, Today, t2.PartnerCost, "Cash", box.Id, null, null, null, null))).EnsureOkAsync();

        // AP/AR: supplier, bill, payment, return; a receivable (e-invoice submission).
        var supplier = await ReadAsync<ApArPartyResponse>(await c.PostAsJsonAsync(U("/apar/parties"),
            new ApArPartyRequest("Supplier", marker + " Fuel", "100200300", "0791112223", "Amman", acc["2101"].Id, 30, null, null)));
        var bill = await ReadAsync<ApArDocumentDetails>(await c.PostAsJsonAsync(U("/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, "88412",
            [new ApArLineRequest(marker + " diesel", acc["5103"].Id, 3120m, 16m, cc.Id)], null)));
        await (await c.PostAsJsonAsync(U("/apar/payments"), new ApArPaymentRequest(bill.Document.Id, Today, 232m, "Cash", box.Id, null, null, 0, null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U("/apar/returns"), new ApArReturnRequest(bill.Document.Id, Today, 100m, marker + " wrong grade", null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U("/apar/receivables"), new ApArDocumentRequest(null, customer.Id, Today, null, marker,
            [new ApArLineRequest(marker + " truck rental", acc["4102"].Id, 1400m, 16m, null)], null))).EnsureOkAsync();

        // Accounting: a manual entry, a draft, a template, a budget; a pending posting (written directly: it comes from a failed posting).
        ManualEntryRequest Entry() => new(Today, marker + " entry",
            [new JournalLineRequest(acc["5203"].Id, 50m, 0m, cc.Id, null, null), new JournalLineRequest(acc["110101"].Id, 0m, 50m, null, null, null)], null);
        await (await c.PostAsJsonAsync(U("/accounting/journal-entries"), Entry())).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U("/accounting/journal-drafts"), Entry())).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U("/accounting/journal-templates"), new JournalTemplateRequest(marker + " template", null, null,
            [new JournalTemplateLineRequest(acc["5203"].Id, 50m, 0m, null, null), new JournalTemplateLineRequest(acc["110101"].Id, 0m, 50m, null, null)]))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U("/accounting/budgets"), new CreateBudgetRequest(Today.Year, marker + " plan"))).EnsureOkAsync();
        var ids = await f.DbAsync(db => db.Set<Core.User>().IgnoreQueryFilters().Where(u => u.Email == o.Email).Select(u => new { T = u.TenantId!.Value }).SingleAsync());
        var companyId = await f.DbAsync(db => db.Set<Core.Company>().IgnoreQueryFilters().Where(x => x.TenantId == ids.T).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync());
        await f.DbAsync(db => db.Database.ExecuteSqlRawAsync("INSERT INTO [acc].[PendingPostings] ([TenantId], [CompanyId], [SourceModule], [SourceType], [SourceId], [Reason], [Attempts]) " +
            "VALUES ({0}, {1}, 'ACC', 'Manual', 0, 'sweep', 1)", ids.T, companyId));

        // Configuration and collaboration: a saved report and its dashboard pin, a custom field, a role, an invitation, a record lock, an export.
        var report = await ReadAsync<SavedReportResponse>(await c.PostAsJsonAsync(U("/reports/saved"), new SavedReportRequest(marker + " report", "trips",
            new ReportDefinition([new ReportGrouping("customer", null)], null, [new ReportMeasure("count", null)], null, null, null, false, null), false, null)));
        await (await c.PostAsJsonAsync(U("/dashboard/pins"), new DashboardPinRequest(report.Id, 0, null, null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U("/custom-fields/TRN.Customer"), new CustomFieldRequest("sweepMark", "علامة", "Mark", "Text", null, null, null, false, false, null, false, null, null)))
            .EnsureOkAsync();
        var role = await ReadAsync<RoleResponse>(await c.PostAsJsonAsync(U("/roles"), new RoleRequest(marker + " clerk", [PermissionKeys.TripsView], null)));
        await (await c.PostAsJsonAsync(U("/invitations"), new InviteRequest($"{marker.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test", role.Id))).EnsureOkAsync();
        var seqs = (await c.GetFromJsonAsync<List<JsonElement>>(U("/settings/number-sequences")))!;
        await (await c.PostAsJsonAsync(U("/locks"), new AcquireLockRequest("CFG.NumberSequence", seqs[0].GetProperty("id").GetString()!))).EnsureOkAsync();
        var job = await ReadAsync<ExportJobResponse>(await c.PostAsJsonAsync(U("/exports"), new ExportRequest("builder", "customers",
            new ReportDefinition(null, null, null, null, null, null, true, ["name", "city"]), null, null, null, null, null, null, "csv")));
        await new ExportWorker(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Services.GetRequiredService<IExportQueue>(), NullLogger<ExportWorker>.Instance)
            .DrainAsync(CancellationToken.None);

        // Provider side: a support request (also a notification to the Owner) and an unpaid subscription invoice.
        var tenant = (await ReadAsync<List<SupportTenantOption>>(await staff.Client.GetAsync("/api/v1/platform/support-access/tenants?q=" + Uri.EscapeDataString(o.Email))))
            .Single(t => t.OwnerEmail == o.Email).Id;
        await (await staff.Client.PostAsJsonAsync("/api/v1/platform/support-access", new SupportAccessRequest(tenant, marker + " support"))).EnsureOkAsync();
        await (await c.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new { planId })).EnsureOkAsync();
        // The second company: the demo company with its sample data.
        await (await c.PostAsJsonAsync("/api/v1/tenant/demo-company", new { })).EnsureOkAsync();
        var me = await ReadAsync<MeResponse>(await c.GetAsync("/api/v1/me"));
        return new Seeded(o, ids.T, me.Companies.Select(x => x.Id).ToList(), job.Id);
    }
}
