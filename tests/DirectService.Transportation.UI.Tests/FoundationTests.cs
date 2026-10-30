using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Platform.UI.Layout;
using DirectService.Shared.UI.Layout;
using DirectService.Transportation.Web.Client.Localization;
using DirectService.Platform.UI.Localization;
using DirectService.Shared.UI.Localization;
using DirectService.Transportation.Web.Client.Ui;
using DirectService.Shared.UI.Ui;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Text catalogue, bidi isolation, formats, search folding and the permission-filtered menu.</summary>
public sealed class FoundationTests
{
    [Fact]
    public void Every_interface_text_has_Arabic_and_English()
    {
        var missing = UiText.All.Where(kv => string.IsNullOrWhiteSpace(kv.Value.Ar) || string.IsNullOrWhiteSpace(kv.Value.En)).Select(kv => kv.Key).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_status_tag_and_menu_entry_has_its_text()
    {
        var keys = Statuses.All.Keys.Select(k => "st." + k)
            .Concat(NavModel.Company.Select(g => g.Key)).Concat(NavModel.Company.SelectMany(g => g.Items).Select(i => i.Key))
            .Concat(NavModel.Console.Select(c => c.Key));
        Assert.All(keys, k => Assert.True(UiText.All.ContainsKey(k), k));
    }

    [Fact]
    public void Text_used_by_the_components_exists()
    {
        // Every key referenced in the screens and components (L["…"], L.F("…")) must be in the catalogue; keys built at run time
        // (a prefix followed by "+ value") are left out.
        var root = FindRepoRoot();
        var sources = UiFolders(root).SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(p => (p.EndsWith(".razor", StringComparison.Ordinal) || p.EndsWith(".cs", StringComparison.Ordinal)) && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var used = sources.SelectMany(p => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(p), "L(?:\\.F\\(|\\[)\"([a-z]+\\.[A-Za-z0-9_.-]*[A-Za-z0-9])\"(?!\\s*\\+)").Select(m => m.Groups[1].Value)).Distinct().ToList();
        Assert.NotEmpty(used);
        Assert.All(used, k => Assert.True(UiText.All.ContainsKey(k), k));
    }

    [Theory]
    [InlineData("الرحلة TRP-10481 بتاريخ 2026-10-10", new[] { "TRP-10481", "2026-10-10" })]
    [InlineData("فولفو FH · 32 t", new[] { "FH · 32 t" })]
    [InlineData("المبلغ 1,624.000 دينار", new[] { "1,624.000" })]
    [InlineData("IBAN JO71 CBJO 0010", new[] { "IBAN JO71 CBJO 0010" })]
    [InlineData("حد النقص 0.5٪", new[] { "0.5٪" })]
    [InlineData("بدون أرقام", new string[0])]
    public void Left_to_right_runs_are_isolated_as_one_unit(string text, string[] runs)
    {
        var parts = Bidi.Split(text).ToList();
        Assert.Equal(runs, parts.Where(p => p.Ltr).Select(p => p.Text).ToArray());
        Assert.Equal(text, string.Concat(parts.Select(p => p.Text)));
    }

    [Theory]
    [InlineData(1624.5, 3, "1,624.500")]
    [InlineData(0, 3, "0.000")]
    [InlineData(-12.345, 3, "-12.345")]
    [InlineData(1234567.891, 2, "1,234,567.89")]
    [InlineData(15, 0, "15")]
    public void Amounts_use_Western_digits_and_the_currency_decimals(double value, int decimals, string expected) =>
        Assert.Equal(expected, Fmt.Money((decimal)value, decimals));

    [Theory]
    [InlineData("١٬٢٣٤٫٥", 1234.5)]
    [InlineData("1,234.500", 1234.5)]
    [InlineData("  ٣١٫٨٥ ", 31.85)]
    [InlineData("-7", -7)]
    public void Typed_amounts_accept_Arabic_Indic_digits(string text, double expected)
    {
        Assert.True(Fmt.TryParseDecimal(text, out var v));
        Assert.Equal((decimal)expected, v);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("12,34")]
    [InlineData("abc")]
    public void Badly_grouped_or_invalid_amounts_are_refused(string text) => Assert.False(Fmt.TryParseDecimal(text, out _));

    [Theory]
    [InlineData("/c/abc/invoices/x", "/c/abc/invoices/x")]
    [InlineData("//evil.example", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("https://evil.example", null)]
    public void Links_from_data_stay_inside_the_application(string href, string? expected) => Assert.Equal(expected, SafeHref.Of(href));

    [Fact]
    public void Only_offered_date_formats_are_used()
    {
        Assert.Equal("dd/MM/yyyy", Fmt.Safe("dd/MM/yyyy"));
        Assert.Equal("yyyy-MM-dd", Fmt.Safe("'<script>'"));
        Assert.Equal("10/10/2026", Fmt.Date(new DateOnly(2026, 10, 10), "dd/MM/yyyy"));
    }

    [Theory]
    [InlineData("أحمد صالح", "احمد")]
    [InlineData("شركة البوتاس", "شركه")]
    [InlineData("مصطفى", "مصطفي")]
    [InlineData("مُحَمَّد", "محمد")]
    [InlineData("TRP-١٠٤٨١", "trp-10481")]
    public void Search_folding_matches_the_server_rules(string text, string typed) => Assert.True(Fold.Contains(text, typed));

    [Fact]
    public void A_dispatcher_sees_operations_but_not_accounting_settings_or_tenant()
    {
        var menu = NavModel.For([PermissionKeys.TripsView, PermissionKeys.TripsEdit, PermissionKeys.DrvView], setupIncomplete: false);
        var keys = menu.Select(g => g.Key).ToList();
        Assert.Contains("nav.operations", keys);
        Assert.DoesNotContain("nav.accounting", keys);
        Assert.DoesNotContain("nav.settings", keys);
        Assert.DoesNotContain("nav.tenant", keys);
        Assert.DoesNotContain("nav.setup", keys);
        Assert.Equal(["nav.driverAccounts", "nav.partnerAccounts"], menu.Single(g => g.Key == "nav.billing").Items.Select(i => i.Key));
    }

    [Fact]
    public void The_setup_checklist_shows_while_setup_is_incomplete_and_tenant_only_with_tenant_manage()
    {
        var all = typeof(PermissionKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        var menu = NavModel.For(all, setupIncomplete: true);
        Assert.Contains(menu, g => g.Key == "nav.setup");
        Assert.Contains(menu, g => g.Key == "nav.tenant");
        Assert.Equal("/c/abc/trips/new", NavModel.Href("abc", "trips/new"));
        Assert.Equal("/c/abc", NavModel.Href("abc", ""));
        Assert.Equal("/tenant/export", NavModel.Href("abc", "/tenant/export"));
    }

    private sealed class Line { public decimal Amount { get; set; } }

    [Fact]
    public void Api_field_paths_find_the_last_segment_and_the_line()
    {
        var model = new object();
        var ctx = new Microsoft.AspNetCore.Components.Forms.EditContext(model);
        var l0 = new Line();
        var l1 = new Line();
        FieldRegistry.Register(ctx, new(model, "Date"), "f1");
        FieldRegistry.Register(ctx, new(l0, nameof(Line.Amount)), "f2");
        FieldRegistry.Register(ctx, new(l1, nameof(Line.Amount)), "f3");
        Assert.Equal("Date", FieldRegistry.Find(ctx, "date")!.Value.FieldName);
        Assert.Same(l1, FieldRegistry.Find(ctx, "lines[1].amount")!.Value.Model);
        Assert.Same(l0, FieldRegistry.Find(ctx, "lines[0].amount")!.Value.Model);
        Assert.Null(FieldRegistry.Find(ctx, "lines[5].amount"));
        Assert.Null(FieldRegistry.Find(ctx, "address.city"));
    }

    [Fact]
    public void Help_pages_cover_every_Section_9_flow_in_both_languages()
    {
        var topics = DirectService.Shared.UI.Help.HelpTopics.All;
        Assert.Equal(Enumerable.Range(1, 14).Select(i => $"9.{i}"), topics.Take(14).Select(t => t.Prd));
        // Owner change requests of 15 Oct 2026, part B ("Rev. 6"), and owner-feedback pages named by their requirements (DT-210: "FR-176/178").
        Assert.All(topics.Skip(14), t => Assert.True(t.Prd == "Rev. 6" || t.Prd.StartsWith("FR-", StringComparison.Ordinal), t.Key));
        Assert.Equal(topics.Count, topics.Select(t => t.Key).Distinct().Count());
        Assert.All(topics, t =>
        {
            Assert.Equal(t.Ar.Steps.Count, t.En.Steps.Count);
            Assert.Equal(t.Ar.Problems.Count, t.En.Problems.Count);
            Assert.All(new[] { t.Ar.Title, t.Ar.Summary, t.Ar.Who, t.En.Title, t.En.Summary, t.En.Who }.Concat(t.Ar.Steps).Concat(t.En.Steps),
                s => Assert.False(string.IsNullOrWhiteSpace(s), t.Key));
            Assert.NotEmpty(t.Screens);
            Assert.All(t.Screens, s => Assert.True(UiText.All.ContainsKey(s.LabelKey), $"{t.Key}: {s.LabelKey}"));
        });
    }

    [Fact]
    public void Screens_link_only_to_existing_help_pages()
    {
        var root = FindRepoRoot();
        var pages = UiFolders(root, "Pages").SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories));
        var helped = pages.Where(p => File.ReadAllText(p).Contains("<PageHeader Help=", StringComparison.Ordinal)).ToList();
        Assert.True(helped.Count >= 30, $"{helped.Count} screens link to help");
        foreach (var page in helped)
        {
            var attr = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(page), "<PageHeader Help=\"(@\\([^)]*\\)|[^\"]*)\"").Groups[1].Value;
            var keys = attr.StartsWith('@')
                ? System.Text.RegularExpressions.Regex.Matches(attr, "\"([a-z-]+)\"").Select(m => m.Groups[1].Value).ToList()
                : [attr];
            Assert.NotEmpty(keys);
            Assert.All(keys, k => Assert.True(DirectService.Shared.UI.Help.HelpTopics.Find(k) is not null, $"{Path.GetFileName(page)}: {k}"));
        }
    }

    [Fact]
    public void Every_permission_group_of_the_seed_has_its_heading_in_both_languages()
    {
        // Role editor (owner feedback SCR-76): the stored English group codes (DT-09) are shown through usr.grp.<code>.
        // The rows are seeded by the module that owns the key (Stage 2 session 11): the platform's own and this module's.
        var seed = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Platform", "DirectService.Platform.Infrastructure", "Persistence", "Seed", "PlatformReferenceDataSeed.cs"))
            + File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "DirectService.Transportation.Infrastructure", "Persistence", "Seed", "ReferenceDataSeed.cs"));
        var groups = System.Text.RegularExpressions.Regex.Matches(seed, "\"[a-z]+\\.[a-z]+(?:\\.[a-z]+)?\", \"([^\"]+)\",").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(9, groups.Count);
        Assert.All(groups, g => Assert.True(UiText.All.ContainsKey("usr.grp." + g), g));
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DirectService.Transportation.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>The source folders of the browser app and its UI libraries (Stage 2 session 15): the source checks read every one of them.</summary>
    internal static IEnumerable<string> UiFolders(string root, params string[] sub) =>
        UiProjects.Select(p => Path.Combine([root, "src", .. p, .. sub])).Where(Directory.Exists);

    private static readonly string[][] UiProjects = [["DirectService.Transportation.Web.Client"], ["Shared", "DirectService.Shared.UI"], ["Platform", "DirectService.Platform.UI"]];
}
