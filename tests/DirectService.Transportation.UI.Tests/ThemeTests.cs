using System.Text.RegularExpressions;
using AngleSharp.Dom;
using DirectService.Platform.Contracts.Identity;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Shared.UI.Layout;
using DirectService.Transportation.Web.Client.Pages;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// DT-244: the display density is gone (the comfortable look is the only one); the theme — Follow device (default), Light, Dark — is chosen in
/// the user menu and on the profile page, applied at once and remembered per user on this device. Colours come only from the tokens.
/// </summary>
public sealed partial class ThemeTests : PageTestBase
{
    private static readonly string[] Order = ["system", "light", "dark"];

    private IRenderedComponent<AppShell> RenderShellWithMenu()
    {
        var cut = Render<AppShell>();
        Act(() => cut.Find($"button[aria-label='{L["shell.userMenu"]}']").Click());
        return cut;
    }

    private static IReadOnlyList<IElement> ThemeItems(IRenderedComponent<AppShell> cut) => cut.FindAll("[role=menuitemradio][data-theme-option]");

    private JSRuntimeInvocation LastCall(string name) => UiModule.Invocations.Last(i => i.Identifier == name);

    private string? LastApplied() => UiModule.Invocations.Where(i => i.Identifier == "applyTheme").Select(i => i.Arguments[0] as string).LastOrDefault();

    private IElement ThemeSelect(IRenderedComponent<Profile> cut) => cut.FindAll("select").Single(s => s.TextContent.Contains(L["shell.themeDark"], StringComparison.Ordinal));

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void The_user_menu_offers_the_three_themes_and_no_density(string language)
    {
        L.SetLanguage(language);
        var cut = RenderShellWithMenu();

        var items = ThemeItems(cut);
        Assert.Equal(Order, items.Select(i => i.GetAttribute("data-theme-option")));
        Assert.Equal(new[] { L["shell.themeSystem"], L["shell.themeLight"], L["shell.themeDark"] }, items.Select(i => i.TextContent.Trim()));
        Assert.Contains($"{L["shell.theme"]} · {L["shell.themeNote"]}", cut.Find(".menu").TextContent, StringComparison.Ordinal);
        // Follow device is the default.
        Assert.Equal("true", items[0].GetAttribute("aria-checked"));
        Assert.All(items.Skip(1), i => Assert.Equal("false", i.GetAttribute("aria-checked")));
        // No density anywhere: not in the menu, not in the texts, not read from the device.
        Assert.DoesNotContain(language == "en" ? "density" : "كثافة", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("shell.density", L["shell.density"]);
        Assert.DoesNotContain(UiModule.Invocations, i => i.Identifier == "getPref" && Equals(i.Arguments[0], "density"));
    }

    [Fact]
    public void Choosing_dark_applies_it_at_once_and_saves_it_for_this_user()
    {
        var cut = RenderShellWithMenu();
        Act(() => cut.Find("[data-theme-option=dark]").Click());

        Assert.Equal("dark", Shell.Theme);
        var saved = LastCall("setPref");
        Assert.Equal(new object?[] { "theme.u1", "dark" }, saved.Arguments);
        Assert.Equal("dark", LastApplied());

        Act(() => cut.Find($"button[aria-label='{L["shell.userMenu"]}']").Click());
        Assert.Equal("true", cut.Find("[data-theme-option=dark]").GetAttribute("aria-checked"));
    }

    [Theory]
    [InlineData("light", "light")]
    [InlineData("dark", "dark")]
    [InlineData(null, "system")]        // nothing saved for this user: follow the device
    [InlineData("comfortable", "system")]   // anything unreadable (e.g. an old density value) is ignored
    public void The_saved_choice_of_the_user_is_applied_when_the_shell_starts(string? stored, string applied)
    {
        UiModule.Setup<string?>("getPref", "theme.u1").SetResult(stored);
        var cut = Render<AppShell>();

        cut.WaitForAssertion(() => Assert.Equal(applied, LastApplied()));
        Assert.Equal(applied, Shell.Theme);
        Act(() => cut.Find($"button[aria-label='{L["shell.userMenu"]}']").Click());
        Assert.Equal("true", cut.Find($"[data-theme-option={applied}]").GetAttribute("aria-checked"));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void The_profile_page_has_the_theme_instead_of_the_density_and_applies_it_without_saving(string language)
    {
        L.SetLanguage(language);
        Respond("/me/2fa", new TwoFactorStatusResponse(false, false, false, 0, null));
        Respond("/me/sessions", Array.Empty<SessionResponse>());
        var cut = Render<Profile>(p => p.Add(x => x.Company, Company));

        cut.WaitForAssertion(() => Assert.Contains(L["profile.tfaTitle"], Text(cut)));
        var select = ThemeSelect(cut);
        Assert.Equal(new[] { L["shell.themeSystem"], L["shell.themeLight"], L["shell.themeDark"] }, select.QuerySelectorAll("option").Select(o => o.TextContent.Trim()));
        Assert.Equal("0", select.GetAttribute("value"));   // Follow device
        Assert.Contains(L["shell.theme"], cut.Markup, StringComparison.Ordinal);
        Assert.Contains(L["shell.themeNote"], cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(cut.FindAll("option"), o => o.TextContent.Contains(language == "en" ? "Compact" : "مضغوط", StringComparison.Ordinal));

        var before = Requests.Count;
        Act(() => ThemeSelect(cut).Change("1"));   // Light
        Assert.Equal("light", Shell.Theme);
        Assert.Equal(new object?[] { "theme.u1", "light" }, LastCall("setPref").Arguments);
        Assert.Equal("light", LastApplied());
        Assert.DoesNotContain(Requests.Skip(before), r => r.Method != "GET");   // a device preference: nothing is sent to the server
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        return Path.Combine([dir!.FullName, "src", .. parts]);
    }

    [GeneratedRegex(@"#[0-9A-Fa-f]{3,8}\b|rgba?\(|\b(?:color|background|fill|stroke|border(?:-color)?)\s*:\s*(?:white|black)\b")]
    private static partial Regex Colour();

    [Fact]
    public void No_page_component_or_style_sheet_has_a_colour_of_its_own()
    {
        var found = new List<string>();
        var files = FoundationTests.UiFolders(FoundationTests.FindRepoRoot()).SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Source("DirectService.Transportation.Web", "Components"), "*.razor", SearchOption.AllDirectories))
            .Append(Source("Shared", "DirectService.Shared.UI", "wwwroot", "css", "app.css"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Colour().IsMatch(lines[i])) found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(found.Count == 0, "use a colour token from tokens.css:\n" + string.Join("\n", found));
    }

    [GeneratedRegex(@"var\(--([a-z0-9-]+)")]
    private static partial Regex TokenUse();

    [GeneratedRegex(@"(?<![\w(-])--([a-z0-9-]+)\s*:")]
    private static partial Regex TokenDefinition();

    [Fact]
    public void Every_token_in_use_is_defined_and_the_dark_theme_is_for_the_screen_only()
    {
        var tokens = File.ReadAllText(Source("Shared", "DirectService.Shared.UI", "wwwroot", "css", "tokens.css"));
        var defined = TokenDefinition().Matches(tokens).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var app = File.ReadAllText(Source("Shared", "DirectService.Shared.UI", "wwwroot", "css", "app.css"));
        Assert.All(TokenUse().Matches(app).Select(m => m.Groups[1].Value).Distinct(), t => Assert.True(defined.Contains(t), $"--{t} is not defined in tokens.css"));

        // Printing (and every print preview) keeps the light tokens: the dark block sits inside @media screen.
        var dark = tokens.IndexOf(":root[data-theme=\"dark\"]", StringComparison.Ordinal);
        Assert.True(dark > tokens.IndexOf("@media screen", StringComparison.Ordinal) && tokens.IndexOf("@media screen", StringComparison.Ordinal) > 0);
        Assert.Equal(1, Regex.Count(tokens, @"\[data-theme=""dark""\]\s*\{"));   // one dark rule
        Assert.DoesNotContain("data-density", tokens, StringComparison.Ordinal);
        Assert.DoesNotContain("--doc-bg", tokens[dark..], StringComparison.Ordinal);   // documents and QR codes stay white
    }
}
