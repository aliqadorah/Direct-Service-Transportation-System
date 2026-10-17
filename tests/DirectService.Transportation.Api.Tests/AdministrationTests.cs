using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Api.Tests;

/// <summary>B1 administration: users/roles (FR-172…174, BR-001…003), companies (5.1, D-09, Q-06), settings (FR-168…171), locks (BR-039).</summary>
[Collection(ApiCollection.Name)]
public sealed class AdministrationTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    [Fact]
    public async Task Last_owner_cannot_be_disabled_or_demoted_BR001()
    {
        var owner = await f.SignUpOwnerAsync();
        var r = await owner.Client.PostAsJsonAsync(U(owner, $"/users/{owner.Me.Id}/disable"), new ReasonRequest("test"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("user.lastOwner", await r.ProblemCodeAsync());

        var roles = await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles"));
        var admin = roles!.Single(x => x.Name == PresetRoles.Administrator);
        var demote = await owner.Client.PutAsJsonAsync(U(owner, $"/users/{owner.Me.Id}/role"), new AssignRoleRequest(admin.Id));
        Assert.Equal("user.lastOwner", await demote.ProblemCodeAsync());
    }

    [Fact]
    public async Task Each_company_keeps_a_user_manager_BR002_and_role_change_ends_sessions()
    {
        var owner = await f.SignUpOwnerAsync();
        var admin = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Administrator);
        var roles = await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles"));
        var viewer = roles!.Single(x => x.Name == PresetRoles.Viewer);

        // Promote the administrator to Owner, then the original owner can be demoted to Viewer: an Owner and a user manager remain.
        var ownerRole = roles!.Single(x => x.Name == PresetRoles.Owner);
        await (await owner.Client.PutAsJsonAsync(U(owner, $"/users/{admin.Me.Id}/role"), new AssignRoleRequest(ownerRole.Id))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Client.GetAsync("/api/v1/me")).StatusCode); // SEC-03

        var adminAgain = await f.SignInAsync(admin.Email, ApiHelpers.Password, admin.AuthenticatorKey);
        await (await adminAgain.PutAsJsonAsync(U(owner, $"/users/{owner.Me.Id}/role"), new AssignRoleRequest(viewer.Id))).EnsureOkAsync();

        // Now the only user manager is adminAgain: demoting them to Viewer must fail (BR-001 owner and BR-002 manager).
        var self = await adminAgain.PutAsJsonAsync(U(owner, $"/users/{admin.Me.Id}/role"), new AssignRoleRequest(viewer.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, self.StatusCode);
    }

    [Fact]
    public async Task Users_are_archived_never_deleted_BR003_and_actions_are_audited_with_reason()
    {
        var owner = await f.SignUpOwnerAsync();
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Dispatcher);
        await (await owner.Client.PostAsJsonAsync(U(owner, $"/users/{dispatcher.Me.Id}/archive"), new ReasonRequest("Left the company"))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await dispatcher.Client.GetAsync("/api/v1/me")).StatusCode);
        await f.DbAsync(async db =>
        {
            var u = await db.Set<User>().IgnoreQueryFilters().SingleAsync(x => x.Email == dispatcher.Email);
            Assert.Equal("Archived", u.Status);
            Assert.False(u.IsDeleted);
            var audit = await db.Set<AuditLog>().IgnoreQueryFilters().Where(a => a.EntityType == "CORE.User" && a.EntityId == u.Id && a.Action == "Archive").SingleAsync();
            Assert.Contains("Left the company", audit.NewValues!, StringComparison.Ordinal);
            return true;
        });
        var signIn = f.NewClient();
        await signIn.UseAntiforgeryAsync();
        var r = await signIn.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(dispatcher.Email, ApiHelpers.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Disable_unlock_reset_2fa_and_admin_sign_out()
    {
        var owner = await f.SignUpOwnerAsync();
        var user = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Accountant);
        await (await owner.Client.PostAsync(U(owner, $"/users/{user.Me.Id}/sign-out"), null)).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.Client.GetAsync("/api/v1/me")).StatusCode);

        await (await owner.Client.PostAsJsonAsync(U(owner, $"/users/{user.Me.Id}/reset-2fa"), new ReasonRequest("Lost phone"))).EnsureOkAsync();
        var again = f.NewClient();
        await again.UseAntiforgeryAsync();
        var signIn = await again.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(user.Email, ApiHelpers.Password));
        // Revision 6: 2FA is optional unless the tenant requires it, so after a reset the user signs in and may set it up again later.
        Assert.Equal("done", (await signIn.Content.ReadFromJsonAsync<SignInResponse>())!.Next);

        await (await owner.Client.PostAsJsonAsync(U(owner, $"/users/{user.Me.Id}/disable"), new ReasonRequest("On leave"))).EnsureOkAsync();
        var list = await owner.Client.GetFromJsonAsync<PagedResult<UserListItem>>(U(owner, "/users"));
        Assert.Equal("Disabled", list!.Items.Single(x => x.Id == user.Me.Id).Status);
        await (await owner.Client.PostAsync(U(owner, $"/users/{user.Me.Id}/enable"), null)).EnsureOkAsync();
        await (await owner.Client.PostAsync(U(owner, $"/users/{user.Me.Id}/unlock"), null)).EnsureOkAsync();
    }

    [Fact]
    public async Task Custom_roles_presets_are_fixed_and_in_use_roles_cannot_be_deleted()
    {
        var owner = await f.SignUpOwnerAsync();
        var roles = await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles"));
        var preset = roles!.First(x => x.IsPreset);
        var editPreset = await owner.Client.PutAsJsonAsync(U(owner, $"/roles/{preset.Id}"), new RoleRequest("X", [PermissionKeys.TripsView], null));
        Assert.Equal("role.presetFixed", await editPreset.ProblemCodeAsync());

        var created = await owner.Client.PostAsJsonAsync(U(owner, "/roles"), new RoleRequest("Night clerk", [PermissionKeys.TripsView, PermissionKeys.TripsEdit], null));
        await created.EnsureOkAsync();
        var role = (await created.Content.ReadFromJsonAsync<RoleResponse>())!;
        Assert.Equal([PermissionKeys.TripsEdit, PermissionKeys.TripsView], role.Permissions);

        var dup = await owner.Client.PostAsJsonAsync(U(owner, "/roles"), new RoleRequest("NIGHT CLERK", [PermissionKeys.TripsView], null));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var updated = await owner.Client.PutAsJsonAsync(U(owner, $"/roles/{role.Id}"), new RoleRequest("Night clerk", [PermissionKeys.TripsView], role.RowVersion));
        await updated.EnsureOkAsync();
        var stale = await owner.Client.PutAsJsonAsync(U(owner, $"/roles/{role.Id}"), new RoleRequest("Night clerk 2", [PermissionKeys.TripsView], role.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency.conflict", await stale.ProblemCodeAsync());

        var email = $"clerk-{Guid.NewGuid():N}@example.test";
        await (await owner.Client.PostAsJsonAsync(U(owner, "/invitations"), new InviteRequest(email, role.Id))).EnsureOkAsync();
        var token = f.Emails.LinkParameters(email, "Invitation")["t"];
        var c = f.NewClient();
        await c.UseAntiforgeryAsync();
        await (await c.PostAsJsonAsync("/api/v1/auth/invitations/accept", new InvitationAcceptRequest(token, "Clerk", ApiHelpers.Password))).EnsureOkAsync();

        var del = await owner.Client.PostAsJsonAsync(U(owner, $"/roles/{role.Id}/delete"), new ReasonRequest("not needed"));
        Assert.Equal("role.inUse", await del.ProblemCodeAsync());
    }

    [Fact]
    public async Task Invitations_expire_are_single_use_and_refuse_emails_of_other_tenants()
    {
        var owner = await f.SignUpOwnerAsync();
        var other = await f.SignUpOwnerAsync();
        var roles = await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles"));
        var viewer = roles!.Single(x => x.Name == PresetRoles.Viewer);

        var foreign = await owner.Client.PostAsJsonAsync(U(owner, "/invitations"), new InviteRequest(other.Email, viewer.Id));
        Assert.Equal("user.emailInUse", await foreign.ProblemCodeAsync()); // A-18

        var email = $"inv-{Guid.NewGuid():N}@example.test";
        await (await owner.Client.PostAsJsonAsync(U(owner, "/invitations"), new InviteRequest(email, viewer.Id))).EnsureOkAsync();
        var token = f.Emails.LinkParameters(email, "Invitation")["t"];
        var anon = f.NewClient();
        await anon.UseAntiforgeryAsync();
        var info = await anon.GetFromJsonAsync<InvitationInfoResponse>($"/api/v1/auth/invitations/{Uri.EscapeDataString(token)}");
        Assert.True(info!.IsValid);
        Assert.Equal(email, info.Email);
        await (await anon.PostAsJsonAsync("/api/v1/auth/invitations/accept", new InvitationAcceptRequest(token, "Invitee", ApiHelpers.Password))).EnsureOkAsync();
        var twice = await anon.PostAsJsonAsync("/api/v1/auth/invitations/accept", new InvitationAcceptRequest(token, "Invitee", ApiHelpers.Password));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, twice.StatusCode);

        var list = await owner.Client.GetFromJsonAsync<List<InvitationResponse>>(U(owner, "/invitations"));
        var accepted = list!.Single(i => i.Email == email);
        Assert.Equal("Accepted", accepted.Status);
        Assert.InRange((accepted.ExpiresAt - DateTime.UtcNow).TotalDays, 6.9, 7.01); // ASM-08
    }

    [Fact]
    public async Task Companies_respect_the_plan_limit_and_archive_rules()
    {
        var owner = await f.SignUpOwnerAsync();
        var second = await owner.Client.PostAsJsonAsync("/api/v1/tenant/companies", new CreateCompanyRequest("Second Co", "Second Co"));
        await second.EnsureOkAsync();
        var co2 = (await second.Content.ReadFromJsonAsync<CompanyResponse>())!;

        var third = await owner.Client.PostAsJsonAsync("/api/v1/tenant/companies", new CreateCompanyRequest("Third Co", null));
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode); // TRIAL plan: 2 companies (DT-08)
        Assert.Equal("plan.limitReached", await third.ProblemCodeAsync());

        await (await owner.Client.PostAsJsonAsync($"/api/v1/tenant/companies/{co2.Id}/archive", new ReasonRequest("Merged"))).EnsureOkAsync();
        var last = await owner.Client.PostAsJsonAsync($"/api/v1/tenant/companies/{owner.CompanyId}/archive", new ReasonRequest("x"));
        Assert.Equal("company.lastActive", await last.ProblemCodeAsync());

        // archived company: the Owner can still read it, but writes are refused (D-09)
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync($"/api/v1/companies/{co2.Id}/settings/profile")).StatusCode);
        var write = await owner.Client.PutAsJsonAsync($"/api/v1/companies/{co2.Id}/settings/operations",
            new OperationSettingsRequest(true, false, 0.5m, false, true, true, false));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal("company.archived", await write.ProblemCodeAsync());

        // archived companies do not count: a new one can be created (Q-06)
        await (await owner.Client.PostAsJsonAsync("/api/v1/tenant/companies", new CreateCompanyRequest("Third Co", null))).EnsureOkAsync();
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.Equal(3, me!.Companies.Count);
        Assert.All(me.Companies, c => { Assert.Equal("JOD", c.CurrencyCode); Assert.Equal(3, c.CurrencyDecimals); });   // currency for every screen
    }

    [Fact]
    public async Task Tenant_password_policy_is_applied()
    {
        var owner = await f.SignUpOwnerAsync();
        var tenant = await owner.Client.GetFromJsonAsync<TenantResponse>("/api/v1/tenant");
        Assert.Equal(8, tenant!.PasswordMinLength);
        var tooShort = await owner.Client.PutAsJsonAsync("/api/v1/tenant/security", new TenantSecurityRequest(6, true, true, 30, tenant.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode); // Q-05: never below 8
        await (await owner.Client.PutAsJsonAsync("/api/v1/tenant/security", new TenantSecurityRequest(12, true, true, 15, tenant.RowVersion))).EnsureOkAsync();

        var roles = await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles"));
        var email = $"pw-{Guid.NewGuid():N}@example.test";
        await (await owner.Client.PostAsJsonAsync(U(owner, "/invitations"), new InviteRequest(email, roles!.Single(x => x.Name == PresetRoles.Viewer).Id))).EnsureOkAsync();
        var token = f.Emails.LinkParameters(email, "Invitation")["t"];
        var c = f.NewClient();
        await c.UseAntiforgeryAsync();
        var weak = await c.PostAsJsonAsync("/api/v1/auth/invitations/accept", new InvitationAcceptRequest(token, "P", "Short1pass"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        await (await c.PostAsJsonAsync("/api/v1/auth/invitations/accept", new InvitationAcceptRequest(token, "P", "LongerPassw0rd"))).EnsureOkAsync();
    }

    [Fact]
    public async Task Settings_follow_FR169_to_FR171()
    {
        var owner = await f.SignUpOwnerAsync();
        var ops = await owner.Client.GetFromJsonAsync<OperationSettingsResponse>(U(owner, "/settings/operations"));
        Assert.True(ops!.BillOnUnloadedWeight);
        Assert.False(ops.PayDriverOnLoadedWeight);
        Assert.Equal(0.5m, ops.ShortageTolerancePct);
        Assert.True(ops.InvoiceApproval);
        Assert.True(ops.AccrueDriverWage);

        var unconfirmed = await owner.Client.PutAsJsonAsync(U(owner, "/settings/operations"), new OperationSettingsRequest(false, false, 0.5m, false, true, true, false));
        Assert.Equal("confirmation.required", await unconfirmed.ProblemCodeAsync()); // FR-170
        await (await owner.Client.PutAsJsonAsync(U(owner, "/settings/operations"), new OperationSettingsRequest(false, false, 0.8m, true, true, true, true))).EnsureOkAsync();

        var seqs = await owner.Client.GetFromJsonAsync<List<NumberSequenceResponse>>(U(owner, "/settings/number-sequences"));
        var inv = seqs!.Single(s => s.DocumentType == "TRN.Invoice");
        Assert.Equal("INV-00001", inv.NextFormatted);
        var updated = await owner.Client.PutAsJsonAsync(U(owner, $"/settings/number-sequences/{inv.Id}"), new NumberSequenceRequest("INV-", 6, 100, inv.RowVersion));
        await updated.EnsureOkAsync();
        var seq = (await updated.Content.ReadFromJsonAsync<NumberSequenceResponse>())!;
        Assert.Equal("INV-000100", seq.NextFormatted);
        var below = await owner.Client.PutAsJsonAsync(U(owner, $"/settings/number-sequences/{inv.Id}"), new NumberSequenceRequest("INV-", 6, 99, seq.RowVersion));
        Assert.Equal("numbering.belowIssued", await below.ProblemCodeAsync()); // BR-038a

        var notConfirmed = await owner.Client.PostAsJsonAsync(U(owner, "/settings/migration-complete"), new MigrationCompleteRequest(false));
        Assert.Equal("confirmation.required", await notConfirmed.ProblemCodeAsync());
        await (await owner.Client.PostAsJsonAsync(U(owner, "/settings/migration-complete"), new MigrationCompleteRequest(true))).EnsureOkAsync();
        var twice = await owner.Client.PostAsJsonAsync(U(owner, "/settings/migration-complete"), new MigrationCompleteRequest(true));
        Assert.Equal("settings.migrationComplete", await twice.ProblemCodeAsync()); // FR-171 irreversible

        var profile = await owner.Client.GetFromJsonAsync<CompanyProfileResponse>(U(owner, "/settings/profile"));
        var badPhone = await owner.Client.PutAsJsonAsync(U(owner, "/settings/profile"),
            new CompanyProfileRequest(profile!.Name, null, "123", null, null, "0512345678", null, profile.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, badPhone.StatusCode);
        await (await owner.Client.PutAsJsonAsync(U(owner, "/settings/profile"),
            new CompanyProfileRequest(profile.Name, "Name EN", "١٢٣٤٥", null, null, "٠٧٩١٢٣٤٥٦٧", "info@example.test", profile.RowVersion))).EnsureOkAsync();
        var saved = await owner.Client.GetFromJsonAsync<CompanyProfileResponse>(U(owner, "/settings/profile"));
        Assert.Equal("12345", saved!.TaxNumber);   // Arabic-Indic digits stored as ASCII (A-19 note)
        Assert.Equal("0791234567", saved.Phone);
    }

    [Fact]
    public async Task Record_lock_is_held_by_one_user_and_released()
    {
        var owner = await f.SignUpOwnerAsync();
        var admin = await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Administrator);
        var seqs = await owner.Client.GetFromJsonAsync<List<NumberSequenceResponse>>(U(owner, "/settings/number-sequences"));
        var target = new AcquireLockRequest("CFG.NumberSequence", seqs![0].Id);

        var mine = await owner.Client.PostAsJsonAsync(U(owner, "/locks"), target);
        await mine.EnsureOkAsync();
        var lockInfo = (await mine.Content.ReadFromJsonAsync<LockResponse>())!;
        Assert.True(lockInfo.HeldByMe);

        var theirs = await admin.Client.PostAsJsonAsync(U(owner, "/locks"), target);
        Assert.Equal((HttpStatusCode)423, theirs.StatusCode);
        Assert.Contains("Test Owner", (await theirs.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("detail").GetString(), StringComparison.Ordinal);

        await (await owner.Client.PutAsync(U(owner, $"/locks/{lockInfo.Id}/heartbeat"), null)).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.DeleteAsync(U(owner, $"/locks/{lockInfo.Id}"))).StatusCode);
        await (await owner.Client.DeleteAsync(U(owner, $"/locks/{lockInfo.Id}"))).EnsureOkAsync();
        await (await admin.Client.PostAsJsonAsync(U(owner, "/locks"), target)).EnsureOkAsync();
    }
}
