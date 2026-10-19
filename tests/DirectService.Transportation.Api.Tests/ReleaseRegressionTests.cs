using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;
using static DirectService.Transportation.Api.Tests.PartBSupport;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Release regression of 18 Oct 2026: the separate "Post journal entries" permission (DT-201) and the user-administration guard across
/// companies (security review 18 Oct 2026, DT-202).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReleaseRegressionTests(ApiFactory f)
{
    private static ManualEntryRequest Entry(Dictionary<string, AccountResponse> a, decimal amount = 25) =>
        new(Today, "Rent", [new JournalLineRequest(a["5202"].Id, amount, 0, null, null, null), new JournalLineRequest(a["110101"].Id, 0, amount, null, null, null)], null);

    [Fact]
    public async Task Preparer_saves_drafts_and_templates_but_only_a_poster_posts()
    {
        var (o, a) = await CompanyAsync(f);
        var preparer = await MemberAsync(f, o, PermissionKeys.AcctView, PermissionKeys.AcctPost);

        var draft = await OkAsync<JournalDraftResponse>(await preparer.PostAsJsonAsync(U(o, "/accounting/journal-drafts"), Entry(a)));
        await (await preparer.PostAsJsonAsync(U(o, "/accounting/journal-templates"),
            new JournalTemplateRequest("Rent", null, null, [new JournalTemplateLineRequest(a["5202"].Id, 0, 0, null, null)]))).EnsureOkAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.PostAsJsonAsync(U(o, "/accounting/journal-entries"), Entry(a))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await preparer.PostAsJsonAsync(U(o, $"/accounting/journal-drafts/{draft.Id}/post"), new RowVersionRequest(draft.RowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await preparer.GetAsync(U(o, "/accounting/journal-entries/import/template"))).StatusCode);

        // The posting key alone does not prepare; with both, the preparer's draft is posted.
        var postOnly = await MemberAsync(f, o, PermissionKeys.AcctView, PermissionKeys.AcctPostEntry);
        Assert.Equal(HttpStatusCode.Forbidden, (await postOnly.PostAsJsonAsync(U(o, "/accounting/journal-entries"), Entry(a))).StatusCode);
        var poster = await MemberAsync(f, o, PermissionKeys.AcctView, PermissionKeys.AcctPost, PermissionKeys.AcctPostEntry);
        var entry = await OkAsync<JournalEntryResponse>(
            await poster.PostAsJsonAsync(U(o, $"/accounting/journal-drafts/{draft.Id}/post"), new RowVersionRequest(draft.RowVersion)));
        Assert.Equal("Posted", entry.Status);
        await (await poster.PostAsJsonAsync(U(o, "/accounting/journal-entries"), Entry(a, 10))).EnsureOkAsync();
    }

    [Fact]
    public async Task Preset_roles_with_journal_preparation_also_post()
    {
        foreach (var role in new[] { PresetRoles.Owner, PresetRoles.Administrator, PresetRoles.Accountant })
            Assert.Contains(PermissionKeys.AcctPostEntry, PresetRoles.Permissions[role]);
        Assert.DoesNotContain(PermissionKeys.AcctPostEntry, PresetRoles.Permissions[PresetRoles.Viewer]);

        var owner = await f.SignUpOwnerAsync();
        var roles = (await owner.Client.GetFromJsonAsync<List<RoleResponse>>($"/api/v1/companies/{owner.CompanyId}/roles"))!;
        Assert.Contains(PermissionKeys.AcctPostEntry, roles.Single(r => r.Name == PresetRoles.Accountant).Permissions);
    }

    /// <summary>
    /// Owner testing 18 Oct 2026 (DT-208): "Add user" with a 7-character password showed no field. The organisation's rules are served to
    /// the form, and the server's refusal names the password field with a message built from the policy (one per rule combination).
    /// </summary>
    [Fact]
    public async Task Create_user_password_errors_name_the_field_and_the_organisation_rules()
    {
        var owner = await f.SignUpOwnerAsync();
        var u = $"/api/v1/companies/{owner.CompanyId}/users";
        Assert.Equal(new PasswordPolicyResponse(8, true, true), await owner.Client.GetFromJsonAsync<PasswordPolicyResponse>($"{u}/password-policy"));
        var roles = (await owner.Client.GetFromJsonAsync<List<RoleResponse>>($"/api/v1/companies/{owner.CompanyId}/roles"))!;
        var viewer = roles.Single(r => r.Name == PresetRoles.Viewer).Id;

        async Task<(string? Code, string Body)> CreateAsync(string password)
        {
            var r = await owner.Client.PostAsJsonAsync(u, new CreateUserRequest("Viewer - مطلع", "v" + Guid.NewGuid().ToString("N")[..8], null, password, viewer, true));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var body = await r.Content.ReadAsStringAsync();
            Assert.Contains("\"field\":\"password\"", body, StringComparison.Ordinal);   // on the password field, not a page-level message
            return (await r.ErrorCodeAsync(), body);
        }

        var (code, body) = await CreateAsync("Abcdef1");   // the owner's 7 characters
        Assert.Equal("auth.passwordPolicy", code);
        Assert.Contains("at least 8 characters, including at least one letter and one digit", body, StringComparison.Ordinal);

        // A tenant policy without the letter rule: the message says exactly what applies.
        var tenant = (await owner.Client.GetFromJsonAsync<TenantResponse>("/api/v1/tenant"))!;
        await (await owner.Client.PutAsJsonAsync("/api/v1/tenant/security", new TenantSecurityRequest(10, false, true, 30, tenant.RowVersion))).EnsureOkAsync();
        Assert.Equal(new PasswordPolicyResponse(10, false, true), await owner.Client.GetFromJsonAsync<PasswordPolicyResponse>($"{u}/password-policy"));
        (code, body) = await CreateAsync("123456789");
        Assert.Equal("auth.passwordPolicyDigit", code);
        Assert.Contains("at least 10 characters, including at least one digit.", body, StringComparison.Ordinal);

        // Users without users.manage cannot read the rules through this route.
        var viewerClient = await MemberAsync(f, owner, PermissionKeys.TripsView);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.GetAsync($"{u}/password-policy")).StatusCode);
    }

    [Fact]
    public async Task Administrator_of_one_company_cannot_reset_a_user_who_has_a_role_in_another_company()
    {
        var owner = await f.SignUpOwnerAsync();
        var roles = (await owner.Client.GetFromJsonAsync<List<RoleResponse>>($"/api/v1/companies/{owner.CompanyId}/roles"))!;
        string Role(string name) => roles.Single(r => r.Name == name).Id;
        async Task<(string Name, UserListItem User)> CreateAsync(string role)
        {
            var name = "g." + Guid.NewGuid().ToString("N")[..10];
            var r = await owner.Client.PostAsJsonAsync($"/api/v1/companies/{owner.CompanyId}/users", new CreateUserRequest("Guard", name, null, ApiHelpers.Password, Role(role), false));
            return (name, await OkAsync<UserListItem>(r));
        }

        var (adminName, _) = await CreateAsync(PresetRoles.Administrator);
        var (targetName, target) = await CreateAsync(PresetRoles.Accountant);
        var (_, plain) = await CreateAsync(PresetRoles.Accountant);

        // The target is also Administrator of a second company of the tenant (where the first administrator has no role).
        var coName = "Guard Co " + Guid.NewGuid().ToString("N")[..6];
        await (await owner.Client.PostAsJsonAsync("/api/v1/tenant/companies", new CreateCompanyRequest(coName, null))).EnsureOkAsync();
        await f.DbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [core].[UserCompanyRoles] ([TenantId], [UserId], [CompanyId], [RoleId], [CreatedAt], [CreatedBy])
            SELECT ucr.[TenantId], ucr.[UserId], c.[Id], r.[Id], ucr.[CreatedAt], ucr.[CreatedBy]
            FROM [core].[UserCompanyRoles] ucr
            JOIN [core].[Users] u ON u.[Id] = ucr.[UserId]
            JOIN [core].[Companies] c ON c.[TenantId] = ucr.[TenantId] AND c.[Name] = {coName}
            JOIN [core].[Roles] r ON r.[TenantId] = ucr.[TenantId] AND r.[Name] = N'Administrator' AND r.[IsPreset] = 1 AND r.[IsDeleted] = 0
            WHERE u.[UserName] = {targetName}
            """));

        var admin = f.NewClient();
        await admin.UseAntiforgeryAsync();
        Assert.Equal("twoFactorOffer", (await OkAsync<SignInResponse>(await admin.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, adminName)))).Next);
        await (await admin.PostAsync("/api/v1/auth/2fa/skip", null)).EnsureOkAsync();
        await admin.UseAntiforgeryAsync();

        var u = $"/api/v1/companies/{owner.CompanyId}/users";
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"{u}/{target.Id}/set-password", new SetPasswordRequest("N3w-Password-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"{u}/{target.Id}/reset-2fa", new ReasonRequest("Lost phone"))).StatusCode);
        // The role in the administrator's own company is still theirs to change (company-scoped action).
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{u}/{target.Id}/role", new AssignRoleRequest(Role(PresetRoles.Viewer)))).StatusCode);
        // A user who exists only in the administrator's company is still managed as before; the Owner manages both.
        await (await admin.PostAsJsonAsync($"{u}/{plain.Id}/set-password", new SetPasswordRequest("N3w-Password-1"))).EnsureOkAsync();
        await (await owner.Client.PostAsJsonAsync($"{u}/{target.Id}/set-password", new SetPasswordRequest("N3w-Password-1"))).EnsureOkAsync();
    }
}
