using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Web.Endpoints;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Security and isolation (TST-06, SEC-05/06, SEC-22, API-03): runs on every build and grows with every module.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityTests(ApiFactory f)
{
    private static IEnumerable<RouteEndpoint> ApiEndpoints(ApiFactory f) =>
        f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1", StringComparison.Ordinal) == true);

    [Fact]
    public void Every_company_endpoint_declares_a_permission_or_member_access()
    {
        var missing = ApiEndpoints(f)
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/v1/companies/{company}", StringComparison.Ordinal))
            .Where(e => e.Metadata.GetMetadata<PermissionMetadata>() is null && e.Metadata.GetMetadata<MemberEndpointMetadata>() is null)
            .Select(e => $"{string.Join(',', e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_api_endpoint_is_authorised_or_explicitly_anonymous()
    {
        var open = ApiEndpoints(f)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(e => e.RoutePattern.RawText).ToList();
        Assert.Empty(open);
        var anonymous = ApiEndpoints(f).Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText!).ToList();
        Assert.All(anonymous, p => Assert.StartsWith("/api/v1/auth/", p, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_api_endpoint_has_an_openapi_summary()
    {
        var missing = ApiEndpoints(f)
            .SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Select(m => $"{m} {e.RoutePattern.RawText}"))
            .Where(k => !ApiSummaries.All.ContainsKey(k))
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task Another_tenants_company_is_not_found_on_every_company_endpoint()
    {
        var a = await f.SignUpOwnerAsync();
        var b = await f.SignUpOwnerAsync();
        foreach (var e in ApiEndpoints(f).Where(e => e.RoutePattern.RawText!.StartsWith("/api/v1/companies/{company}", StringComparison.Ordinal)))
        {
            var url = Fill(e.RoutePattern.RawText!, a.CompanyId);
            var acceptsForm = e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes
                .Any(c => c.StartsWith("multipart/", StringComparison.Ordinal)) == true;
            foreach (var method in e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
            {
                HttpContent body = acceptsForm
                    ? new MultipartFormDataContent { { new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]), "file", "logo.png" } }
                    : JsonContent.Create(new { });
                using var req = new HttpRequestMessage(new HttpMethod(method), url) { Content = body };
                var r = await b.Client.SendAsync(req);
                Assert.True(r.StatusCode == HttpStatusCode.NotFound, $"{method} {url} as another tenant returned {(int)r.StatusCode}");
            }
        }
    }

    [Fact]
    public async Task Tampered_or_wrong_type_tokens_are_not_found()
    {
        var a = await f.SignUpOwnerAsync();
        var tampered = a.CompanyId[..^2] + (a.CompanyId[^2] == 'A' ? "B" : "A") + a.CompanyId[^1];
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync($"/api/v1/companies/{tampered}/settings/profile")).StatusCode);
        // the user's own id is a valid token, but of another entity type
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync($"/api/v1/companies/{a.Me.Id}/settings/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync("/api/v1/companies/not-a-token/settings/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync($"/api/v1/companies/{a.CompanyId}/settings/profile")).StatusCode);
    }

    [Fact]
    public async Task A_role_without_the_permission_gets_403()
    {
        var owner = await f.SignUpOwnerAsync();
        var viewer = await InviteAndAcceptAsync(f, owner, PresetRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync($"/api/v1/companies/{owner.CompanyId}/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync($"/api/v1/companies/{owner.CompanyId}/settings/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync("/api/v1/tenant")).StatusCode);
        // member endpoint: allowed for every role (no logo uploaded yet → 404, but never 403)
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.Client.GetAsync($"/api/v1/companies/{owner.CompanyId}/logo")).StatusCode);
    }

    [Fact]
    public async Task An_administrator_cannot_escalate_to_owner_level_SR01()
    {
        var owner = await f.SignUpOwnerAsync();
        var admin = await InviteAndAcceptAsync(f, owner, PresetRoles.Administrator);
        var u = $"/api/v1/companies/{owner.CompanyId}";
        var roles = await admin.Client.GetFromJsonAsync<List<RoleResponse>>($"{u}/roles");
        var ownerRole = roles!.Single(r => r.Name == PresetRoles.Owner);

        // promote self to Owner
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PutAsJsonAsync($"{u}/users/{admin.Me.Id}/role", new AssignRoleRequest(ownerRole.Id))).StatusCode);
        // create a role holding tenant.manage
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PostAsJsonAsync($"{u}/roles",
            new RoleRequest("Shadow owner", [PermissionKeys.TenantManage], null))).StatusCode);
        // invite someone as Owner
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PostAsJsonAsync($"{u}/invitations",
            new InviteRequest($"x-{Guid.NewGuid():N}@example.test", ownerRole.Id))).StatusCode);
        // act on the Owner
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PostAsJsonAsync($"{u}/users/{owner.Me.Id}/disable", new ReasonRequest("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PostAsJsonAsync($"{u}/users/{owner.Me.Id}/reset-2fa", new ReasonRequest("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.PostAsync($"{u}/users/{owner.Me.Id}/password-reset", null)).StatusCode);
        // but the administrator can still manage users below them
        var viewerRole = roles!.Single(r => r.Name == PresetRoles.Viewer);
        await (await admin.Client.PostAsJsonAsync($"{u}/invitations", new InviteRequest($"v-{Guid.NewGuid():N}@example.test", viewerRole.Id))).EnsureOkAsync();
        await (await admin.Client.PostAsJsonAsync($"{u}/roles", new RoleRequest("Clerk", [PermissionKeys.TripsView], null))).EnsureOkAsync();
    }

    [Fact]
    public async Task Writes_without_an_antiforgery_token_are_refused()
    {
        var owner = await f.SignUpOwnerAsync();
        var client = f.NewClient();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, ApiHelpers.Password));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("auth.antiforgery", await r.ProblemCodeAsync());
    }

    [Fact]
    public async Task Problems_are_returned_in_the_users_language()
    {
        var ar = f.NewClient("ar");
        await ar.UseAntiforgeryAsync();
        var r = await ar.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest("x@example.test", "nope1234"));
        var body = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("urn:dst:problem:auth.signInFailed", body.GetProperty("type").GetString());
        Assert.Contains("كلمة المرور", body.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_no_internal_ids()
    {
        var owner = await f.SignUpOwnerAsync();
        var r = await owner.Client.GetAsync("/api/v1/me");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        var json = await r.Content.ReadAsStringAsync();
        var internalId = await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().Where(u => u.Email == owner.Email)
            .Select(u => u.Id).SingleAsync());
        Assert.True(internalId > 0);
        Assert.DoesNotContain("\"id\":" + internalId.ToString(System.Globalization.CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\":\"" + internalId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"", json, StringComparison.Ordinal);
        Assert.Equal(22, owner.Me.Id.Length);
    }

    [Fact]
    public void Url_tokens_are_deterministic_type_scoped_and_unguessable()
    {
        var t = f.Services.GetRequiredService<IUrlTokenService>();
        var id = Guid.NewGuid();
        var a = t.Encode<Company>(id);
        Assert.Equal(a, t.Encode<Company>(id));
        Assert.Equal(22, a.Length);
        Assert.Equal(id, t.Decode<Company>(a));
        Assert.NotEqual(id, t.Decode<User>(a));
        Assert.NotEqual(a, t.Encode<User>(id));
        Assert.Null(t.Decode<Company>("short"));
        Assert.DoesNotContain(id.ToString("N")[..8], a, StringComparison.OrdinalIgnoreCase);
    }

    internal static string Fill(string pattern, string companyToken) =>
        System.Text.RegularExpressions.Regex.Replace(pattern.Replace("{company}", companyToken, StringComparison.Ordinal), @"\{[^}]+\}", "AAAAAAAAAAAAAAAAAAAAAA");

    /// <summary>Owner invites a new user with a preset role; the invitee accepts, signs in and sets up 2FA.</summary>
    internal static async Task<SignedInUser> InviteAndAcceptAsync(ApiFactory f, SignedInUser owner, string presetRole)
    {
        var roles = await owner.Client.GetFromJsonAsync<List<RoleResponse>>($"/api/v1/companies/{owner.CompanyId}/roles");
        var role = roles!.Single(r => r.Name == presetRole && r.IsPreset);
        var email = $"{presetRole.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        await (await owner.Client.PostAsJsonAsync($"/api/v1/companies/{owner.CompanyId}/invitations", new InviteRequest(email, role.Id))).EnsureOkAsync();
        var token = f.Emails.LinkParameters(email, "Invitation")["t"];
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        await (await client.PostAsJsonAsync("/api/v1/auth/invitations/accept", new InvitationAcceptRequest(token, presetRole + " User", ApiHelpers.Password))).EnsureOkAsync();
        return await f.SignInFirstTimeAsync(client, email, ApiHelpers.Password);
    }
}
