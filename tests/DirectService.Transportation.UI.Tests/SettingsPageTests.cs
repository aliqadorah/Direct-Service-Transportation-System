using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Pages.Settings;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Settings screens (SCR-70 … SCR-77) after the independent review: an Administrator without core.tenant.manage.</summary>
public sealed class SettingsPageTests() : PageTestBase(PermissionKeys.UsersManage, PermissionKeys.SettingsManage, PermissionKeys.TripsView)
{
    private static readonly string[] EntityTypes = ["TRN.Trip"];
    private static readonly string[] Actions = ["Create", "Update"];

    [Fact]
    public void Audit_log_pages_with_the_applied_filters_only()
    {
        Respond("audit-log/facets", new { entityTypes = EntityTypes, actions = Actions, users = new[] { new { id = "u9", name = "رنا" } } });
        Respond("audit-log", new PagedResult<AuditLogEntry>([new AuditLogEntry(DateTime.UtcNow, "رنا", "Create", "TRN.Trip", "TRP-1", null, [], null)], 1, 50, 120));
        var cut = Render<AuditLog>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("TRP-1", cut.Markup));
        // Choose an action without pressing Apply, then page: the page request keeps the list's filters.
        cut.FindAll("select").First(s => s.InnerHtml.Contains("Create", StringComparison.Ordinal) || s.InnerHtml.Contains(L["hist.Create"], StringComparison.Ordinal)).Change("Update");
        cut.Find($"button[aria-label='{L.F("ui.page", 2)}']").Click();
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.Contains("page=2", StringComparison.Ordinal)));
        Assert.DoesNotContain(Requests, r => r.Path.StartsWith("audit-log?", StringComparison.Ordinal) && r.Path.Contains("action=Update", StringComparison.Ordinal));
    }

    [Fact]
    public void Roles_beyond_the_callers_keys_cannot_be_given_or_edited()
    {
        var owner = new RoleResponse("r1", "Owner", true, [PermissionKeys.TenantManage, PermissionKeys.UsersManage], 1, "A");
        var custom = new RoleResponse("r2", "مدقق", false, [PermissionKeys.TripsView, PermissionKeys.AcctView], 0, "A");
        Respond("roles/permissions", Array.Empty<PermissionItem>());
        Respond("roles", new[] { owner, custom });
        Respond("users", new PagedResult<UserListItem>([new UserListItem("u1", "المالك", "owner@example.test", "Active", new Ref("r1", "Owner"), false, true, true, null, "A")], 1, 50, 1));
        var cut = Render<UsersRoles>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("owner@example.test", cut.Markup));
        cut.Find("tbody tr").Click();
        cut.WaitForAssertion(() => Assert.Contains(L["usr.ownerProtected"], cut.Markup));   // Owner-level user: no actions for an Administrator
        Assert.All(cut.FindAll("option[value='r1'], option[value='r2']"), o => Assert.True(o.HasAttribute("disabled")));

        cut.FindAll("[role=tab]").First(t => t.TextContent.Contains(L["usr.roles"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains("مدقق", cut.Markup));
        cut.FindAll("tbody tr").First(r => r.TextContent.Contains("مدقق", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(L["usr.roleBeyondCaller"], cut.Markup));
        Assert.DoesNotContain(cut.FindAll(".dialog button"), b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal));
    }

    [Fact]
    public void Role_editor_lists_each_permission_as_its_own_row_under_a_translated_group()
    {
        // Owner feedback SCR-76: group headings in the user's language; each row = checkbox + description + key, inside a scrolling list.
        Respond("roles/permissions", new[]
        {
            new PermissionItem(PermissionKeys.TripsView, "trips.view", "Trips", "عرض الرحلات", "View trips"),
            new PermissionItem(PermissionKeys.UsersManage, "users.manage", "Administration", "إدارة المستخدمين", "Manage users"),
        });
        Respond("roles", Array.Empty<RoleResponse>());
        Respond("users", new PagedResult<UserListItem>([], 1, 50, 0));
        var cut = Render<UsersRoles>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains(L["usr.newRole"], StringComparison.Ordinal)));
        cut.FindAll("button").First(b => b.TextContent.Contains(L["usr.newRole"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".perm-list .perm-row").Count));
        Assert.Equal(["الرحلات", "الإدارة"], cut.FindAll(".perm-group > legend").Select(l => l.TextContent.Trim()));
        var row = cut.FindAll(".perm-row")[0];
        Assert.NotNull(row.QuerySelector("input[type=checkbox]"));
        Assert.Contains("عرض الرحلات", row.TextContent, StringComparison.Ordinal);
        Assert.Equal("trips.view", row.QuerySelector(".perm-key")!.TextContent);
        Assert.Equal("0", cut.Find(".perm-list").GetAttribute("tabindex"));   // keyboard users can scroll the list
    }
}
