using System.Net;
using System.Text;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Pages.Settings;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner testing 18 Oct 2026 (DT-209): the trips list showed "connection to the server was lost" for a server error. The error screen now says
/// "connection lost" only when no answer came, "you don't have permission" for 403 and "something went wrong on the server" otherwise, with
/// the reference to quote.
/// </summary>
public sealed class ErrorScreenTests() : PageTestBase(PermissionKeys.UsersManage, PermissionKeys.SettingsManage, PermissionKeys.TripsView)
{
    private static readonly RoleResponse Viewer = new("r3", "Viewer", true, [PermissionKeys.TripsView], 0, "A");

    private static HttpResponseMessage Problem(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") };

    [Theory]
    [InlineData(500, "ui.serverErrorTitle", "ui.serverErrorText")]
    [InlineData(403, "ui.forbiddenTitle", "ui.forbiddenText")]
    [InlineData(0, "ui.loadFailed", "ui.loadFailedText")]
    public void The_error_screen_says_what_happened(int status, string title, string text)
    {
        Respond("roles/permissions", Array.Empty<PermissionItem>());
        Respond("roles", new[] { Viewer });
        Respond("users", _ => status == 0
            ? throw new HttpRequestException("No connection")
            : Problem((HttpStatusCode)status, $$"""{"status":{{status}},"code":"x","detail":"x","traceId":"0HNPDLJIR6489:00000307"}"""));
        var cut = Render<UsersRoles>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L[title], Text(cut)));
        Assert.Contains(L[text], Text(cut));
        if (status != 0) Assert.Contains("00000307", Text(cut));   // the reference to quote
        if (status != 0) Assert.DoesNotContain(L["ui.loadFailedText"], Text(cut));   // not "connection lost"
    }
}
