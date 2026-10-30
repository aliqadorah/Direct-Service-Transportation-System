using System.Reflection;
using System.Text.RegularExpressions;
using DirectService.Platform.Contracts.Collaboration;
using DirectService.Shared.Infrastructure.Security;
using DirectService.Transportation.Web.Realtime;
using DirectService.Transportation.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DirectService.Architecture.Tests;

/// <summary>
/// Names that keep users signed in, stored secrets readable and browsers working (platform design §e, E-21 … E-35): Data Protection, auth
/// schemes, cookies and claims, token providers, SignalR, localStorage, file paths, embedded resources, user-secrets ids.
/// </summary>
public sealed class SecurityAndBrowserTests
{
    private static readonly System.Text.Json.Nodes.JsonNode G = Golden.Security;

    private static string Value(string key) => G[key]!.GetValue<string>();

    private static string Value(string section, string key) => G[section]![key]!.GetValue<string>();

    /// <summary>A pinned value equals its golden value (the golden value is the expected one).</summary>
    private static void Same(string golden, string? now) => Assert.True(golden == now, $"Pinned value changed: golden \"{golden}\", now \"{now}\".");

    private static string Source(string relative) => File.ReadAllText(Path.Combine(Golden.RepoRoot, relative));

    [Fact]
    public void Data_protection_application_name_purposes_and_key_folder_are_unchanged()
    {
        Same(Value("dataProtectionApplicationName"), DataProtectionSetup.ApplicationName);
        Same(Value("dataProtectionDefaultKeysPath"), DataProtectionSetup.DefaultKeysPath);
        Golden.SameSet("Data Protection purposes (E-23)", Golden.Strings(G["dataProtectionPurposes"]), Golden.Constants(typeof(DataProtectionPurposes)).Values);

        // Every protector is created through the pinned purposes, never with an inline literal.
        var inline = Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), "CreateProtector\\(\"")).ToList();
        Assert.True(inline.Count == 0, "CreateProtector with an inline purpose (use DataProtectionPurposes): " + string.Join(", ", inline));
    }

    [Fact]
    public void Auth_schemes_cookies_claims_and_antiforgery_names_are_unchanged()
    {
        Golden.SameMap("Auth schemes (E-25)", Golden.Map(G["authSchemes"]), Golden.Constants(typeof(AuthSchemes)));
        Golden.SameMap("Claims (E-25)", Golden.Map(G["claims"]), Golden.Constants(typeof(AppClaims)));

        // The cookie names as the authentication set-up really configures them.
        var services = new ServiceCollection().AddLogging();
        services.AddAppAuthentication();
        using var sp = services.BuildServiceProvider();
        var cookies = sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        Same(Value("cookieApp"), cookies.Get(AuthSchemes.App).Cookie.Name);
        Same(Value("cookieMfa"), cookies.Get(AuthSchemes.Mfa).Cookie.Name);
        Same(Value("cookieApp"), AuthCookies.App);
        Same(Value("cookieMfa"), AuthCookies.Mfa);
        Same(Value("antiforgeryCookie"), AuthCookies.Antiforgery);
        Same(Value("antiforgeryHeader"), AuthCookies.AntiforgeryHeader);
        var program = Source("src/DirectService.Transportation.Web/Program.cs");
        Assert.Contains("o.HeaderName = AuthCookies.AntiforgeryHeader;", program, StringComparison.Ordinal);
        Assert.Contains("o.Cookie.Name = AuthCookies.Antiforgery;", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_token_providers_and_the_url_token_prefix_are_unchanged()
    {
        Same(Value("identityTokenProviders", "replaySafe"), DirectService.Platform.Infrastructure.Identity.ReplaySafeAuthenticatorTokenProvider.Name);
        Same(Value("identityTokenProviders", "staffInvitationProvider"), DirectService.Platform.Application.Staff.StaffService.InvitationTokenProvider);
        Same(Value("identityTokenProviders", "staffInvitationPurpose"), DirectService.Platform.Application.Staff.StaffService.InvitationPurpose);
        Same(Value("urlTokenInfoPrefix"), UrlTokenService.InfoPrefix);
    }

    [Fact]
    public void SignalR_path_group_methods_and_events_are_unchanged()
    {
        Same(Value("signalRPath"), LiveHub.Path);
        Same(Value("signalRPath"), LiveHubNames.Path);
        Same(Value("signalRGroupOfCompany1"), LiveHub.Group(1));

        // Hub methods as SignalR exposes them (the [HubMethodName], else the method name).
        var methods = typeof(LiveHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName)
            .Select(m => m.GetCustomAttribute<HubMethodNameAttribute>()?.Name ?? m.Name);
        Golden.SameSet("Hub methods (E-27)", Golden.Strings(G["signalRHubMethods"]), methods);
        Golden.SameSet("Hub methods the browser calls", Golden.Strings(G["signalRClientInvokes"]), [LiveHubNames.JoinCompany, LiveHubNames.LeaveCompany]);
        Golden.SameSet("Hub events (E-27)", Golden.Strings(G["signalRServerEvents"]),
            [LiveHubNames.RecordChanged, LiveHubNames.LockChanged, LiveHubNames.Notification, LiveHubNames.SupportEnded]);
        Golden.SameSet("Hub events the browser listens to", Golden.Strings(G["signalRClientEvents"]),
            Regex.Matches(Source("src/Shared/DirectService.Shared.UI/Services/LiveUpdates.cs"), "On<JsonElement>\\(LiveHubNames\\.(\\w+)")
                .Select(m => (string)typeof(LiveHubNames).GetField(m.Groups[1].Value)!.GetRawConstantValue()!));
    }

    [Fact]
    public void Local_storage_keys_are_unchanged()
    {
        var prefKeys = typeof(DirectService.Shared.UI.Services.PrefKeys);
        Same(Value("localStorage", "prefix"), (string)prefKeys.GetField("StoragePrefix")!.GetRawConstantValue()!);
        Same(Value("localStorage", "themeKeyPrefix"), (string)prefKeys.GetField("Theme")!.GetRawConstantValue()!);
        Same(Value("localStorage", "navKeyPrefix"), (string)prefKeys.GetField("Nav")!.GetRawConstantValue()!);
        Assert.Contains($"localStorage.getItem(\"{Value("localStorage", "prefix")}\" + key)", Source("src/Shared/DirectService.Shared.UI/wwwroot/js/ui.js"), StringComparison.Ordinal);
        Assert.Contains($"localStorage.getItem(\"{Value("localStorage", "beforeSignInTheme")}\")", Source("src/Shared/DirectService.Shared.UI/wwwroot/js/theme.js"), StringComparison.Ordinal);
    }

    [Fact]
    public void File_paths_resources_user_secrets_and_the_js_initializer_are_unchanged()
    {
        var export = typeof(DirectService.Shared.Infrastructure.Collaboration.ExportService);
        Same(Value("files", "exportsRoot"), DirectService.Shared.Infrastructure.Collaboration.ExportService.Root);
        var folder = (string)export.GetMethod("Folder", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [7L, 9L, Guid.Parse(Golden.Entities["tokenGuid"]!.GetValue<string>())])!;
        Same(Value("files", "exportFolderOfSample"), folder);
        Same(Value("files", "fileStoreDefaultRoot"), DirectService.Shared.Infrastructure.Files.LocalFileStore.DefaultRoot);
        Same(Value("files", "mailDropFolder"), new DirectService.Platform.Application.Email.SystemEmailOptions().DropFolder);
        Golden.SameSet("Embedded resources of the infrastructure (E-32)", Golden.Strings(G["files"]!["embeddedResources"]),
            typeof(DirectService.Platform.Infrastructure.Email.EmailLogo).Assembly.GetManifestResourceNames());   // moved with EmailLogo (Stage 2 session 11)
        Same(Golden.Strings(G["files"]!["embeddedResources"]).Single(), DirectService.Platform.Infrastructure.Email.EmailLogo.ResourceName);
        Assert.NotEmpty(DirectService.Platform.Infrastructure.Email.EmailLogo.Png);

        string UserSecrets(string csproj) => Regex.Match(Source(csproj), "<UserSecretsId>([^<]+)</UserSecretsId>").Groups[1].Value;
        Same(Value("userSecretsIds", "web"), UserSecrets("src/DirectService.Transportation.Web/DirectService.Transportation.Web.csproj"));
        Same(Value("userSecretsIds", "infrastructure"), UserSecrets("src/Hosts/DirectService.Database/DirectService.Database.csproj"));

        // The Blazor JS initializer must carry the browser app's assembly name (E-21; renamed with the assembly in Stage 4).
        var client = typeof(DirectService.Transportation.Web.Client.Routes).Assembly.GetName().Name!;
        Assert.Equal(Golden.Strings(G["blazorJsInitializer"]).Single(), client + ".lib.module.js");
        Assert.True(File.Exists(Path.Combine(Golden.RepoRoot, "src", client, "wwwroot", client + ".lib.module.js")), "Blazor JS initializer file missing.");

        var views = new[] { Path.Combine(Golden.RepoRoot, "src", client), Path.Combine(Golden.RepoRoot, "src", "Shared", "DirectService.Shared.UI"), Path.Combine(Golden.RepoRoot, "src", "Platform", "DirectService.Platform.UI") }
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "ViewKey=\"([^\"]+)\"").Select(m => m.Groups[1].Value));
        Golden.SameSet("Column view keys (cfg.TableColumnViews.ViewKey, E-15)", Golden.Strings(G["viewKeys"]), views);
    }

    [Fact]
    public void The_design_system_files_are_served_by_the_browser_app_at_their_root_urls()
    {
        // Stage 2 session 15 (DT-368): the files live in Shared.UI, whose static web assets have the base path "/" (not _content/…), so
        // css/app.css, js/ui.js, img/icons.svg … keep their URLs and fingerprints; the browser app keeps only its JS initializer (E-21).
        var www = Path.Combine(Golden.RepoRoot, "src", "Shared", "DirectService.Shared.UI", "wwwroot");
        string[] urls = ["css/app.css", "css/fonts.css", "css/tokens.css", "img/icons.svg", "img/logo.png", "js/theme.js", "js/ui.js"];
        Assert.Equal(urls, Directory.EnumerateFiles(www, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(www, f).Replace('\\', '/')).Order(StringComparer.Ordinal));
        Assert.Contains("<StaticWebAssetBasePath>/</StaticWebAssetBasePath>", Source("src/Shared/DirectService.Shared.UI/DirectService.Shared.UI.csproj"), StringComparison.Ordinal);
        var clientWww = Path.Combine(Golden.RepoRoot, "src", "DirectService.Transportation.Web.Client", "wwwroot");
        Assert.Equal(["DirectService.Transportation.Web.Client.lib.module.js"], Directory.EnumerateFiles(clientWww, "*", SearchOption.AllDirectories).Select(Path.GetFileName));
        var page = Source("src/DirectService.Transportation.Web/Components/App.razor");
        foreach (var u in urls.Where(u => u != "img/icons.svg" && u != "js/ui.js")) Assert.Contains($"@Assets[\"{u}\"]", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Brand_fonts_are_embedded_with_their_logical_names_where_BrandFonts_reads_them()
    {
        // E-32: the fonts moved with BrandFonts from the Application assembly to SharedKernel (Stage 2 session 8, DT-325); these are the
        // LogicalNames the Application assembly had before the move, read with typeof(BrandFonts).Assembly.
        string[] files =
        [
            "ibm-plex-sans-arabic-arabic-400-normal.woff2", "ibm-plex-sans-arabic-arabic-600-normal.woff2", "ibm-plex-sans-arabic-arabic-700-normal.woff2",
            "ibm-plex-sans-arabic-latin-400-normal.woff2", "ibm-plex-sans-arabic-latin-600-normal.woff2", "ibm-plex-sans-arabic-latin-700-normal.woff2",
            "inter-latin-400-normal.woff2", "inter-latin-600-normal.woff2", "inter-latin-700-normal.woff2",
        ];
        Golden.SameSet("Embedded brand fonts (E-32)", files.Select(f => "fonts/" + f),
            typeof(BrandFonts).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("fonts/", StringComparison.Ordinal)));
        Assert.Equal(files.Length, BrandFonts.Css.Split("data:font/woff2;base64,").Length - 1);
    }
}
