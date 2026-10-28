using System.Reflection;
using System.Text.RegularExpressions;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Infrastructure.Security;
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
        Same(Value("identityTokenProviders", "replaySafe"), DirectService.Transportation.Infrastructure.Identity.ReplaySafeAuthenticatorTokenProvider.Name);
        Same(Value("identityTokenProviders", "staffInvitationProvider"), DirectService.Transportation.Application.Platform.StaffService.InvitationTokenProvider);
        Same(Value("identityTokenProviders", "staffInvitationPurpose"), DirectService.Transportation.Application.Platform.StaffService.InvitationPurpose);
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
            Regex.Matches(Source("src/DirectService.Transportation.Web.Client/Services/LiveUpdates.cs"), "On<JsonElement>\\(LiveHubNames\\.(\\w+)")
                .Select(m => (string)typeof(LiveHubNames).GetField(m.Groups[1].Value)!.GetRawConstantValue()!));
    }

    [Fact]
    public void Local_storage_keys_are_unchanged()
    {
        var prefKeys = typeof(DirectService.Transportation.Web.Client.Services.PrefKeys);
        Same(Value("localStorage", "prefix"), (string)prefKeys.GetField("StoragePrefix")!.GetRawConstantValue()!);
        Same(Value("localStorage", "themeKeyPrefix"), (string)prefKeys.GetField("Theme")!.GetRawConstantValue()!);
        Same(Value("localStorage", "navKeyPrefix"), (string)prefKeys.GetField("Nav")!.GetRawConstantValue()!);
        Assert.Contains($"localStorage.getItem(\"{Value("localStorage", "prefix")}\" + key)", Source("src/DirectService.Transportation.Web.Client/wwwroot/js/ui.js"), StringComparison.Ordinal);
        Assert.Contains($"localStorage.getItem(\"{Value("localStorage", "beforeSignInTheme")}\")", Source("src/DirectService.Transportation.Web.Client/wwwroot/js/theme.js"), StringComparison.Ordinal);
    }

    [Fact]
    public void File_paths_resources_user_secrets_and_the_js_initializer_are_unchanged()
    {
        var export = typeof(DirectService.Transportation.Application.Collaboration.ExportService);
        Same(Value("files", "exportsRoot"), DirectService.Transportation.Application.Collaboration.ExportService.Root);
        var folder = (string)export.GetMethod("Folder", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [7L, 9L, Guid.Parse(Golden.Entities["tokenGuid"]!.GetValue<string>())])!;
        Same(Value("files", "exportFolderOfSample"), folder);
        Same(Value("files", "fileStoreDefaultRoot"), DirectService.Transportation.Infrastructure.Files.LocalFileStore.DefaultRoot);
        Same(Value("files", "mailDropFolder"), new DirectService.Transportation.Application.Platform.SystemEmailOptions().DropFolder);
        Golden.SameSet("Embedded resources of the infrastructure (E-32)", Golden.Strings(G["files"]!["embeddedResources"]),
            typeof(DataProtectionPurposes).Assembly.GetManifestResourceNames());
        Same(Golden.Strings(G["files"]!["embeddedResources"]).Single(), DirectService.Transportation.Infrastructure.Email.EmailLogo.ResourceName);
        Assert.NotEmpty(DirectService.Transportation.Infrastructure.Email.EmailLogo.Png);

        string UserSecrets(string csproj) => Regex.Match(Source(csproj), "<UserSecretsId>([^<]+)</UserSecretsId>").Groups[1].Value;
        Same(Value("userSecretsIds", "web"), UserSecrets("src/DirectService.Transportation.Web/DirectService.Transportation.Web.csproj"));
        Same(Value("userSecretsIds", "infrastructure"), UserSecrets("src/Hosts/DirectService.Database/DirectService.Database.csproj"));

        // The Blazor JS initializer must carry the browser app's assembly name (E-21; renamed with the assembly in Stage 4).
        var client = typeof(DirectService.Transportation.Web.Client.Services.PrefKeys).Assembly.GetName().Name!;
        Assert.Equal(Golden.Strings(G["blazorJsInitializer"]).Single(), client + ".lib.module.js");
        Assert.True(File.Exists(Path.Combine(Golden.RepoRoot, "src", client, "wwwroot", client + ".lib.module.js")), "Blazor JS initializer file missing.");

        var views = Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "src", client), "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "ViewKey=\"([^\"]+)\"").Select(m => m.Groups[1].Value));
        Golden.SameSet("Column view keys (cfg.TableColumnViews.ViewKey, E-15)", Golden.Strings(G["viewKeys"]), views);
    }
}
