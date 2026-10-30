using System.Net.Http.Json;
using DirectService.Platform.Contracts.Identity;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner feedback 13 Oct 2026: the screen shown while the WebAssembly application downloads is in the user's saved language (the
/// host page renders the profile's language into &lt;html lang&gt;, which the client also reads before its first render).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HostPageTests(ApiFactory f)
{
    [Fact]
    public async Task Boot_screen_follows_the_saved_language()
    {
        var o = await f.SignUpOwnerAsync();
        var page = System.Net.WebUtility.HtmlDecode(await o.Client.GetStringAsync($"/c/{o.CompanyId}/trips"));
        Assert.Contains("<html lang=\"en\" dir=\"ltr\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"app-boot\"", page, StringComparison.Ordinal);
        Assert.Contains("Loading…", page, StringComparison.Ordinal);
        Assert.DoesNotContain("جارٍ التحميل", page, StringComparison.Ordinal);

        await (await o.Client.PutAsJsonAsync("/api/v1/me/preferences", new PreferencesRequest("ar", null, o.Me.DateFormat))).EnsureOkAsync();
        page = System.Net.WebUtility.HtmlDecode(await o.Client.GetStringAsync($"/c/{o.CompanyId}/trips"));
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\"", page, StringComparison.Ordinal);
        Assert.Contains("جارٍ التحميل…", page, StringComparison.Ordinal);

        // Account pages render on the server without the WebAssembly runtime: no boot screen there.
        Assert.DoesNotContain("app-boot", await f.NewClient().GetStringAsync("/sign-in"), StringComparison.Ordinal);
    }
}
