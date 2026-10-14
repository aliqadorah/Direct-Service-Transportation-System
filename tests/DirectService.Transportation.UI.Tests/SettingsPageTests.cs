using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Configuration;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Invoicing;
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

    /// <summary>Owner feedback 13 Oct 2026: one click on a tab shows that tab's data once the (slow) answer arrives.</summary>
    [Fact]
    public void Field_configuration_tab_shows_its_data_after_one_click()
    {
        ResponseDelay = TimeSpan.FromMilliseconds(40);
        Respond("standard-fields/TRN.Trip", new[] { new StandardFieldResponse("receiptNo", "رقم السند", "Receipt no.", null, true, true, false, true, 1) });
        Respond("standard-fields/TRN.Driver", new[] { new StandardFieldResponse("nationalId", "الرقم الوطني", "National ID", null, false, false, false, true, 1) });
        Respond("custom-fields/", Array.Empty<CustomFieldResponse>());
        var cut = Render<FieldConfiguration>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("رقم السند", cut.Markup));
        cut.FindAll("[role=tab]").Single(t => t.TextContent.Contains(L["nav.drivers"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains("الرقم الوطني", cut.Markup));
        Assert.DoesNotContain("رقم السند", cut.Markup);
        Assert.Equal("true", cut.FindAll("[role=tab]").Single(t => t.TextContent.Contains(L["nav.drivers"], StringComparison.Ordinal)).GetAttribute("aria-selected"));
    }

    // ------------------------------------------------------------ field order arrows (owner request 14 Oct 2026, DT-186)

    private static readonly StandardFieldResponse[] TripFields =
    [
        new("receiptNo", "رقم السند", "Receipt no.", null, true, true, false, true, 5),
        new("tripDate", "تاريخ الرحلة", "Trip date", null, false, true, false, true, 7),
        new("notes", "ملاحظات", "Notes", null, false, false, false, false, 40),
    ];

    private static CustomFieldResponse Custom(string id, string key, string label, int order) =>
        new(id, "TRN.Trip", key, label, null, "List", ["أ", "ب"], null, "مساعدة", false, true, order, false, false, "rv-" + id);

    private IRenderedComponent<FieldConfiguration> Fields(params CustomFieldResponse[] custom)
    {
        Respond("standard-fields/TRN.Trip", TripFields);
        Respond("custom-fields/", custom);
        Respond("custom-fields/field/", _ => custom[0]);
        var cut = Render<FieldConfiguration>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("رقم السند", cut.Markup));
        return cut;
    }

    private static AngleSharp.Dom.IElement Arrow(IRenderedComponent<FieldConfiguration> cut, string label) => cut.Find($"button[aria-label='{label}']");

    private static string[] StandardOrder(IRenderedComponent<FieldConfiguration> cut) =>
        cut.FindAll("table.grid")[0].QuerySelectorAll("tbody tr").Select(r => r.QuerySelector("td")!.TextContent.Trim()).Select(t => t.Split(' ')[0]).ToArray();

    [Fact]
    public void Field_order_arrows_replace_the_number_boxes_with_first_and_last_disabled()
    {
        var cut = Fields(Custom("c1", "customsNo", "الكشف", 10), Custom("c2", "tolls", "الرسوم", 20));
        Assert.Empty(cut.FindAll("input[inputmode=numeric]"));                              // no order boxes in the lists
        var up = Arrow(cut, L.F("fld.moveUp", "رقم السند"));
        Assert.True(up.HasAttribute("disabled"));                                           // first row
        Assert.Equal(L.F("fld.moveUp", "رقم السند"), up.GetAttribute("title"));
        Assert.False(Arrow(cut, L.F("fld.moveDown", "رقم السند")).HasAttribute("disabled")); // a core field moves like the others
        Assert.True(Arrow(cut, L.F("fld.moveDown", "ملاحظات")).HasAttribute("disabled"));   // last row
        Assert.True(Arrow(cut, L.F("fld.moveUp", "الكشف")).HasAttribute("disabled"));
        Assert.True(Arrow(cut, L.F("fld.moveDown", "الرسوم")).HasAttribute("disabled"));
        Assert.True(cut.FindAll("button").First(b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal)).HasAttribute("disabled"));
    }

    [Fact]
    public void Moving_a_standard_field_reorders_at_once_renumbers_and_saves_the_new_order()
    {
        var cut = Fields();
        Assert.Equal(["رقم", "تاريخ", "ملاحظات"], StandardOrder(cut));
        Arrow(cut, L.F("fld.moveDown", "رقم السند")).Click();
        Assert.Equal(["تاريخ", "رقم", "ملاحظات"], StandardOrder(cut));
        Assert.True(Arrow(cut, L.F("fld.moveUp", "تاريخ الرحلة")).HasAttribute("disabled"));   // now first
        Arrow(cut, L.F("fld.moveUp", "ملاحظات")).Click();
        Assert.Equal(["تاريخ", "ملاحظات", "رقم"], StandardOrder(cut));
        Assert.True(Arrow(cut, L.F("fld.moveDown", "رقم السند")).HasAttribute("disabled"));    // now last
        // The preview follows the new order before saving.
        Assert.Equal(["تاريخ الرحلة", "ملاحظات", "رقم السند"], cut.FindAll("[role=group] .lbl").Select(l => l.TextContent.Replace("*", "").Trim()).ToArray());

        cut.FindAll("button").First(b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT" && r.Path == "standard-fields/TRN.Trip"));
        var body = Requests.Single(r => r.Method == "PUT" && r.Path == "standard-fields/TRN.Trip").Body!;
        Assert.Matches("\"fieldKey\":\"tripDate\"[^}]*\"sortOrder\":10", body);
        Assert.Matches("\"fieldKey\":\"notes\"[^}]*\"sortOrder\":20", body);
        Assert.Matches("\"fieldKey\":\"receiptNo\"[^}]*\"sortOrder\":30", body);
        Assert.DoesNotContain(Requests, r => r.Path.StartsWith("custom-fields/field/", StringComparison.Ordinal));   // no custom field moved
    }

    [Fact]
    public void Moving_a_custom_field_saves_the_changed_ones_with_their_values_and_does_not_open_the_dialog()
    {
        var cut = Fields(Custom("c1", "customsNo", "الكشف", 10), Custom("c2", "tolls", "الرسوم", 20), Custom("c3", "seal", "الختم", 30));
        Arrow(cut, L.F("fld.moveUp", "الختم")).Click();
        Assert.Empty(cut.FindAll(".dialog"));                                                // the arrow is not a row click
        var grid = cut.FindAll("table.grid")[1];
        Assert.Equal(["الكشف", "الختم", "الرسوم"], grid.QuerySelectorAll("tbody tr").Select(r => r.QuerySelector("td")!.TextContent.Trim()).ToArray());

        cut.FindAll("button").First(b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Equal(2, Requests.Count(r => r.Method == "PUT" && r.Path.StartsWith("custom-fields/field/", StringComparison.Ordinal))));
        using var c3 = System.Text.Json.JsonDocument.Parse(Requests.Single(r => r.Path == "custom-fields/field/c3").Body!);
        var sent = c3.RootElement;
        Assert.Equal(20, sent.GetProperty("sortOrder").GetInt32());
        Assert.Equal("الختم", sent.GetProperty("labelAr").GetString());                    // the field's other values unchanged
        Assert.Equal(["أ", "ب"], sent.GetProperty("options").EnumerateArray().Select(o => o.GetString()!).ToArray());
        Assert.Equal("مساعدة", sent.GetProperty("helpText").GetString());
        Assert.True(sent.GetProperty("showAsColumn").GetBoolean());
        Assert.Equal("rv-c3", sent.GetProperty("rowVersion").GetString());
        using var c2 = System.Text.Json.JsonDocument.Parse(Requests.Single(r => r.Path == "custom-fields/field/c2").Body!);
        Assert.Equal(30, c2.RootElement.GetProperty("sortOrder").GetInt32());
        Assert.DoesNotContain(Requests, r => r.Path == "custom-fields/field/c1");          // c1 kept 10
    }

    [Fact]
    public void Custom_field_dialog_has_no_order_box()
    {
        var cut = Fields(Custom("c1", "customsNo", "الكشف", 10));
        cut.FindAll("table.grid")[1].QuerySelector("tbody tr")!.Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dialog")));
        Assert.DoesNotContain(cut.FindAll(".dialog label"), l => l.TextContent.Trim() == L["fld.order"]);
    }

    [Fact]
    public void Invoice_layout_designer_tab_shows_the_other_layout_after_one_click()
    {
        ResponseDelay = TimeSpan.FromMilliseconds(40);
        InvoiceLayoutResponse Layout(string type, string title) => new(type, title, null, "None", ["amount"], new Dictionary<string, string>(), null, [], true, true, ["amount"], "A");
        Respond("invoice-layouts/Claim", Layout(InvoiceCodes.Claim, "مطالبة مالية"));
        Respond("invoice-layouts/TaxInvoice", Layout(InvoiceCodes.TaxInvoice, "فاتورة ضريبية"));
        var cut = Render<InvoiceLayoutDesigner>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("مطالبة مالية", cut.Markup));
        cut.FindAll("[role=tab]").Single(t => t.TextContent.Contains(L["inv.layoutTax"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains("فاتورة ضريبية", cut.Markup));
    }
}
