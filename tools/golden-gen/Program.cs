// Golden-file generator, run ONCE against the restore tag pre-modules-final-2026-10-27 in a temporary worktree (never committed there).
// Every value is computed by the tag's own code, or read from the tag's source where the tag has only an inline literal.
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Transportation.Infrastructure.Persistence.Interceptors;
using DirectService.Transportation.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var repo = Path.GetFullPath(args[0]);
var outDir = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outDir);
const string FixedKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";   // bytes 0..31, test-only
var fixedGuid = Guid.Parse("6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b");
var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
string Src(string rel) => File.ReadAllText(Path.Combine(repo, rel));
JsonArray Arr(IEnumerable<string> v) => new(v.Distinct().Order(StringComparer.Ordinal).Select(x => (JsonNode)JsonValue.Create(x)).ToArray());
void Write(string name, JsonNode node) => File.WriteAllText(Path.Combine(outDir, name), node.ToJsonString(json).Replace("\r\n", "\n") + "\n");
string One(string text, string pattern) => Regex.Match(text, pattern).Groups[1].Value is { Length: > 0 } v ? v : throw new InvalidOperationException("not found: " + pattern);
bool NotBuild(string f) => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar);

var domain = typeof(AuditableEntity).Assembly;
var app = typeof(IAppDbContext).Assembly;
var infra = typeof(MasterDbContext).Assembly;
var web = typeof(DirectService.Transportation.Web.Security.AuthSchemes).Assembly;
var contracts = typeof(DirectService.Transportation.Contracts.Security.PermissionKeys).Assembly;

// ---------------------------------------------------------------- entities (E-1, E-2, E-3, E-5, E-6)
var entityTypes = domain.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.Namespace is { } ns && ns.Split('.')[^1] is "Acc" or "Cfg" or "Core" or "Trn")
    .OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
var tokens = new UrlTokenService(Options.Create(new UrlTokenOptions { Key = FixedKey }));
using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
var model = db.Model;
var modelTypes = model.GetEntityTypes().Where(e => !e.IsOwned()).Select(e => e.ClrType).ToHashSet();
var resolverMap = typeof(EntityResolver).GetField("Types", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
var resolverKeys = ((IEnumerable<string>)resolverMap.GetType().GetProperty("Keys").GetValue(resolverMap)).ToHashSet();
var describerType = app.GetType("DirectService.Transportation.Application.Audit.AuditDescriber", true);
var describerKeys = ((IDictionary<string, Type>)describerType.GetField("Types", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Keys.ToHashSet();
// Company export: record types by class name when unique (CompanyExportService.RecordType at the tag).
var exportKeys = model.GetEntityTypes().Select(e => e.ClrType).Where(t => typeof(AuditableEntity).IsAssignableFrom(t))
    .GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
var entities = new JsonArray();
foreach (var t in entityTypes)
{
    var stored = AuditSaveChangesInterceptor.EntityTypeName(Activator.CreateInstance(t));
    var isRecord = typeof(AuditableEntity).IsAssignableFrom(t);
    var tenantDeleted = isRecord && t != typeof(DirectService.Transportation.Domain.Core.Tenant)
        && (typeof(ITenantScoped).IsAssignableFrom(t) || typeof(INullableTenantScoped).IsAssignableFrom(t));
    entities.Add(new JsonObject
    {
        ["storedName"] = stored,
        ["tokenLabel"] = t.Name,                       // tag: HKDF info = "DST-URL:" + Type.Name
        ["token"] = tokens.Encode(t, fixedGuid),
        ["clrType"] = t.FullName,                      // informational only (namespaces change in Stages 2-4)
        ["record"] = isRecord,
        ["inModel"] = modelTypes.Contains(t),
        ["entityResolver"] = resolverKeys.Contains(stored),
        ["auditDescriber"] = describerKeys.Contains(stored),
        ["exportRecordType"] = exportKeys.Contains(t.Name) ? t.Name : null,
        ["tenantDeletion"] = tenantDeleted,
        ["properties"] = Arr(t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)),
    });
}
Write("entities.json", new JsonObject
{
    ["generatedFrom"] = "tag pre-modules-final-2026-10-27 (commit d0ef83d)",
    ["tokenKey"] = FixedKey,
    ["tokenGuid"] = fixedGuid.ToString(),
    ["entities"] = entities,
});

// ---------------------------------------------------------------- stored keys (E-4, E-8 ... E-20)
JsonObject Consts(Type t)
{
    var o = new JsonObject();
    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)).OrderBy(f => f.Name, StringComparer.Ordinal))
        o[f.Name] = (string)f.GetRawConstantValue();
    return o;
}
object Dummy(ParameterInfo p) => p.ParameterType == typeof(string) ? (p.Name == "language" ? "en" : "x@example.com")
    : p.ParameterType == typeof(int) ? 1 : p.ParameterType == typeof(bool) ? false
    : p.ParameterType == typeof(DateTime) || p.ParameterType == typeof(DateTime?) ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    : p.ParameterType == typeof(DateOnly) ? new DateOnly(2026, 1, 1) : null;
var templatesType = app.GetType("DirectService.Transportation.Application.Notifications.EmailTemplates", true);
var templates = new JsonObject();
foreach (var m in templatesType.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType.Name == "EmailMessage").OrderBy(m => m.Name, StringComparer.Ordinal))
{
    var msg = m.Invoke(null, m.GetParameters().Select(Dummy).ToArray());
    templates[m.Name] = (string)msg.GetType().GetProperty("Template").GetValue(msg);
}
var secretLinks = (IEnumerable<string>)templatesType.GetField("SecretLinkTemplates").GetValue(null);
var notifTexts = app.GetType("DirectService.Transportation.Application.Collaboration.NotificationTexts", true);
var notifTypes = (IEnumerable<string>)notifTexts.GetProperty("Types").GetValue(null);
var designModel = db.GetService<IDesignTimeModel>().Model;
JsonArray Seed(Type t, params string[] cols)
{
    var rows = new JsonArray();
    foreach (var r in designModel.FindEntityType(t).GetSeedData().OrderBy(r => (long)r["Id"]))
    {
        var o = new JsonObject();
        foreach (var c in cols) o[c] = r.TryGetValue(c, out var v) ? JsonValue.Create(v is Guid g ? g.ToString() : v?.ToString()) : null;
        rows.Add(o);
    }
    return rows;
}
var presetRoles = contracts.GetType("DirectService.Transportation.Contracts.Security.PresetRoles", true);
var presetGrants = new JsonObject();
foreach (var (role, keys) in (IReadOnlyDictionary<string, IReadOnlyList<string>>)presetRoles.GetField("Permissions").GetValue(null)) presetGrants[role] = Arr(keys);
// Codes held in constants named after their column, wherever they are declared (audit EntityType, posting Source*/Party*).
var codes = new JsonObject();
foreach (var name in new[] { "EntityType", "SourceType", "SourceModule", "PartyType", "PartyModule", "ControlPartyType" })
    codes[name] = Arr(new[] { app, infra, web }.SelectMany(a => a.GetTypes())
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name == name).Select(f => (string)f.GetRawConstantValue()));
var reportSources = app.GetType("DirectService.Transportation.Application.Reports.ReportSources", true);
var sources = new JsonObject();
foreach (var s in (System.Collections.IEnumerable)reportSources.GetField("All").GetValue(null))
{
    var key = (string)s.GetType().GetProperty("Key").GetValue(s);
    var fields = ((System.Collections.IEnumerable)s.GetType().GetProperty("Fields").GetValue(s)).Cast<object>().Select(f => (string)f.GetType().GetProperty("Key").GetValue(f));
    sources[key] = Arr(fields);
}
var clientRoot = Path.Combine(repo, "src", "DirectService.Transportation.Web.Client");
var clientSources = Directory.EnumerateFiles(clientRoot, "*.*", SearchOption.AllDirectories).Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs")) && NotBuild(f)).ToList();
var clientLiterals = clientSources.SelectMany(f => Regex.Matches(File.ReadAllText(f), "\"((?:TRN|ACC|CFG|CORE)\\.[A-Za-z]+)\"").Select(m => m.Groups[1].Value));
Write("stored-keys.json", new JsonObject
{
    ["emailTemplates"] = templates,
    ["emailSecretLinkTemplates"] = Arr(secretLinks),
    ["notificationTypes"] = Arr(notifTypes),
    ["permissionKeys"] = Consts(contracts.GetType("DirectService.Transportation.Contracts.Security.PermissionKeys", true)),
    ["permissionSeed"] = Seed(typeof(DirectService.Transportation.Domain.Core.Permission), "Id", "PublicId", "PermissionKey", "GroupName"),
    ["planSeed"] = Seed(typeof(DirectService.Transportation.Domain.Core.Plan), "Id", "PublicId", "Code"),
    ["presetRoles"] = Consts(presetRoles),
    ["presetRoleGrants"] = presetGrants,
    ["settingKeys"] = Consts(app.GetType("DirectService.Transportation.Application.Configuration.SettingKeys", true)),
    ["documentTypes"] = Consts(app.GetType("DirectService.Transportation.Application.Configuration.DocumentTypes", true)),
    ["postingKeys"] = Consts(app.GetType("DirectService.Transportation.Application.Accounting.PostingKeys", true)),
    ["notificationConstants"] = Consts(app.GetType("DirectService.Transportation.Application.Collaboration.NotificationService", true)),
    ["accountingCodes"] = Consts(app.GetType("DirectService.Transportation.Application.Accounting.AccountingCodes", true)),
    ["accountingCodeLists"] = new JsonObject
    {
        ["AccountTypes"] = new JsonArray(((IEnumerable<string>)app.GetType("DirectService.Transportation.Application.Accounting.AccountingCodes", true).GetField("AccountTypes").GetValue(null)).Select(x => (JsonNode)JsonValue.Create(x)).ToArray()),
        ["ControlPartyTypes"] = new JsonArray(((IEnumerable<string>)app.GetType("DirectService.Transportation.Application.Accounting.AccountingCodes", true).GetField("ControlPartyTypes").GetValue(null)).Select(x => (JsonNode)JsonValue.Create(x)).ToArray()),
    },
    ["fieldCatalogEntityTypes"] = Consts(app.GetType("DirectService.Transportation.Application.Configuration.FieldCatalog", true)),
    ["codes"] = codes,
    ["reportSources"] = sources,
    ["clientStoredNameLiterals"] = Arr(clientLiterals),
});

// ---------------------------------------------------------------- crypto, browser, files (E-21 ... E-35)
var services = new ServiceCollection();
services.AddLogging();
web.GetType("DirectService.Transportation.Web.Security.AuthSetup", true).GetMethod("AddAppAuthentication").Invoke(null, [services]);
using var sp = services.BuildServiceProvider();
var cookies = sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
var program = Src("src/DirectService.Transportation.Web/Program.cs");
var purposes = Directory.EnumerateFiles(Path.Combine(repo, "src"), "*.cs", SearchOption.AllDirectories).Where(NotBuild)
    .SelectMany(f => Regex.Matches(File.ReadAllText(f), "CreateProtector\\(\"([^\"]+)\"\\)").Select(m => m.Groups[1].Value));
var liveHub = web.GetType("DirectService.Transportation.Web.Realtime.LiveHub", true);
var hubSrc = Src("src/DirectService.Transportation.Web/Realtime/LiveHub.cs");
var clientLive = Src("src/DirectService.Transportation.Web.Client/Services/LiveUpdates.cs");
string UserSecrets(string csproj) => One(Src(csproj), "<UserSecretsId>([^<]+)</UserSecretsId>");
var staff = app.GetType("DirectService.Transportation.Application.Platform.StaffService", true);
var mailOptions = app.GetType("DirectService.Transportation.Application.Platform.SystemEmailOptions", true);
Write("security-and-browser.json", new JsonObject
{
    ["dataProtectionApplicationName"] = (string)web.GetType("DirectService.Transportation.Web.Security.DataProtectionSetup", true).GetField("ApplicationName").GetRawConstantValue(),
    ["dataProtectionPurposes"] = Arr(purposes),
    ["dataProtectionDefaultKeysPath"] = One(Src("src/DirectService.Transportation.Web/Security/DataProtectionSetup.cs"), "p : \"([^\"]+)\", env.ContentRootPath"),
    ["authSchemes"] = Consts(web.GetType("DirectService.Transportation.Web.Security.AuthSchemes", true)),
    ["claims"] = Consts(web.GetType("DirectService.Transportation.Web.Security.AppClaims", true)),
    ["cookieApp"] = cookies.Get("dst").Cookie.Name,
    ["cookieMfa"] = cookies.Get("dst-mfa").Cookie.Name,
    ["antiforgeryCookie"] = One(program, "o.Cookie.Name = \"([^\"]+)\""),
    ["antiforgeryHeader"] = One(program, "o.HeaderName = \"([^\"]+)\""),
    ["identityTokenProviders"] = new JsonObject
    {
        ["replaySafe"] = (string)infra.GetType("DirectService.Transportation.Infrastructure.Identity.ReplaySafeAuthenticatorTokenProvider", true).GetField("Name").GetRawConstantValue(),
        ["staffInvitationProvider"] = (string)staff.GetField("InvitationTokenProvider").GetRawConstantValue(),
        ["staffInvitationPurpose"] = (string)staff.GetField("InvitationPurpose").GetRawConstantValue(),
    },
    ["urlTokenInfoPrefix"] = One(Src("src/DirectService.Transportation.Infrastructure/Security/UrlTokenService.cs"), "GetBytes\\(\"([^\"]+)\" \\+ t\\.Name\\)"),
    ["signalRPath"] = (string)liveHub.GetField("Path").GetRawConstantValue(),
    ["signalRGroupOfCompany1"] = (string)liveHub.GetMethod("Group").Invoke(null, [1L]),
    ["signalRHubMethods"] = Arr(liveHub.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName).Select(m => m.Name)),
    ["signalRServerEvents"] = Arr(Regex.Matches(hubSrc, "SendAsync\\(\"(\\w+)\"").Select(m => m.Groups[1].Value)),
    ["signalRClientEvents"] = Arr(Regex.Matches(clientLive, "On<JsonElement>\\(\"(\\w+)\"").Select(m => m.Groups[1].Value)),
    ["signalRClientInvokes"] = Arr(Regex.Matches(clientLive, "InvokeAsync(?:<bool>)?\\(\"(\\w+)\"").Select(m => m.Groups[1].Value)),
    ["localStorage"] = new JsonObject
    {
        ["prefix"] = One(Src("src/DirectService.Transportation.Web.Client/wwwroot/js/ui.js"), "localStorage\\.getItem\\(\"([^\"]+)\" \\+ key\\)"),
        ["beforeSignInTheme"] = One(Src("src/DirectService.Transportation.Web.Client/wwwroot/js/theme.js"), "localStorage\\.getItem\\(\"([^\"]+)\"\\)"),
        ["themeKeyPrefix"] = One(Src("src/DirectService.Transportation.Web.Client/Services/ShellState.cs"), "ThemeKey => \"([^\"]+)\""),
        ["navKeyPrefix"] = One(Src("src/DirectService.Transportation.Web.Client/Layout/SideNav.razor"), "PrefKey => \"([^\"]+)\""),
    },
    ["files"] = new JsonObject
    {
        ["exportsRoot"] = One(Src("src/DirectService.Transportation.Web/Workers/ExportWorker.cs"), "PurgeAsync\\(\"([^\"]+)\""),
        ["exportFolderOfSample"] = (string)app.GetType("DirectService.Transportation.Application.Collaboration.ExportService", true)
            .GetMethod("Folder", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, [7L, 9L, fixedGuid]),
        ["fileStoreDefaultRoot"] = "App_Data/" + One(Src("src/DirectService.Transportation.Infrastructure/Files/LocalFileStore.cs"), "\"App_Data\", \"([^\"]+)\""),
        ["mailDropFolder"] = (string)mailOptions.GetProperty("DropFolder").GetValue(Activator.CreateInstance(mailOptions)),
        ["embeddedResources"] = Arr(infra.GetManifestResourceNames()),
    },
    ["userSecretsIds"] = new JsonObject
    {
        ["web"] = UserSecrets("src/DirectService.Transportation.Web/DirectService.Transportation.Web.csproj"),
        ["infrastructure"] = UserSecrets("src/DirectService.Transportation.Infrastructure/DirectService.Transportation.Infrastructure.csproj"),
    },
    ["blazorJsInitializer"] = Arr(Directory.EnumerateFiles(Path.Combine(clientRoot, "wwwroot"), "*.lib.module.js").Select(Path.GetFileName)),
    ["viewKeys"] = Arr(clientSources.Where(f => f.EndsWith(".razor")).SelectMany(f => Regex.Matches(File.ReadAllText(f), "ViewKey=\"([^\"]+)\"").Select(m => m.Groups[1].Value))),
});

// ---------------------------------------------------------------- OpenAPI (E-36, E-37)
var openapiText = Src("docs/api/openapi-v1.json").Replace("\r\n", "\n");
var openapi = JsonNode.Parse(openapiText);
var paths = openapi["paths"].AsObject();
var ops = paths.Sum(p => p.Value.AsObject().Count(o => o.Key is "get" or "post" or "put" or "patch" or "delete"));
Write("openapi.json", new JsonObject
{
    ["file"] = "docs/api/openapi-v1.json",
    ["sha256Lf"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(openapiText))).ToLowerInvariant(),
    ["title"] = (string)openapi["info"]["title"],
    ["paths"] = paths.Count,
    ["operations"] = ops,
    ["schemas"] = openapi["components"]["schemas"].AsObject().Count,
});
Console.WriteLine($"entities {entities.Count}, templates {templates.Count}, purposes {purposes.Distinct().Count()}, paths {paths.Count}, ops {ops}");
