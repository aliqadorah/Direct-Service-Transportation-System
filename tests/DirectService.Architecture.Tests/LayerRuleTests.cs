using System.Reflection;
using System.Xml.Linq;

namespace DirectService.Architecture.Tests;

/// <summary>
/// Layer and module rules of the platform design (a.2), first set (Stage 2 session 2, DT-290): SharedKernel references only the browser-safe
/// Shared.Contracts (DT-309); Shared.* references no Platform or module project; a Domain project references SharedKernel only. Checked on the project files
/// (every project under src/, empty ones included) and on the compiled assemblies. Exception E-1 (Stage 2 session 11): modules use the Platform
/// anchor types Tenant, Company and User for foreign keys only.
/// </summary>
public sealed class LayerRuleTests
{
    private const string Kernel = "DirectService.SharedKernel";
    private const string SharedContracts = "DirectService.Shared.Contracts";

    /// <summary>Every project under src/ with the names of the projects it references.</summary>
    private static readonly Dictionary<string, List<string>> Projects =
        Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => XDocument.Load(f).Descendants("ProjectReference")
                .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))).ToList(),
                StringComparer.Ordinal);

    private static void Only(string project, Func<string, bool> allowed, string rule)
    {
        var bad = Projects[project].Where(r => !allowed(r)).ToList();
        Assert.True(bad.Count == 0, $"{project} breaks the rule \"{rule}\": it references {string.Join(", ", bad)}");
    }

    [Fact]
    public void The_new_projects_exist_and_are_in_the_solution()
    {
        string[] expected =
        [
            Kernel, SharedContracts, "DirectService.Shared.Infrastructure", "DirectService.Shared.UI",
            "DirectService.Platform.Domain", "DirectService.Platform.Application", "DirectService.Platform.Infrastructure",
            "DirectService.Platform.Contracts", "DirectService.Platform.UI", "DirectService.Database",
        ];
        Assert.Empty(expected.Except(Projects.Keys));
        var solution = File.ReadAllText(Path.Combine(Golden.RepoRoot, "DirectService.Transportation.slnx"));
        var all = Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "tests"), "*.csproj", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}e2e{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Golden.RepoRoot, f).Replace('\\', '/')).ToList();
        var missing = all.Where(p => !solution.Contains($"Path=\"{p}\"", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "Projects not in DirectService.Transportation.slnx: " + string.Join(", ", missing));
    }

    [Fact]
    public void SharedKernel_references_only_the_shared_contracts()
    {
        // DT-309: the browser-safe Shared.Contracts (FieldRef for Validator) is the one project SharedKernel may use.
        Only(Kernel, r => r == SharedContracts, "SharedKernel references only Shared.Contracts");
        var compiled = typeof(StoredNames).Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("DirectService.", StringComparison.Ordinal)).ToList();
        Assert.True(compiled.All(n => n == SharedContracts), "The compiled SharedKernel references " + string.Join(", ", compiled));
    }

    [Fact]
    public void Shared_contracts_are_browser_safe()
    {
        // No project and no package: the browser app loads it (DT-309).
        Only(SharedContracts, _ => false, "Shared.Contracts references nothing of the solution");
        var file = Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "src", "Shared", SharedContracts), "*.csproj").Single();
        Assert.Empty(XDocument.Load(file).Descendants("PackageReference"));
        var compiled = typeof(FieldRef).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n != "netstandard" && n != "mscorlib").ToList();
        Assert.True(compiled.Count == 0, "The compiled Shared.Contracts references " + string.Join(", ", compiled));
    }

    [Fact]
    public void Platform_contracts_are_browser_safe()
    {
        // The browser app references Platform.Contracts (the identity and platform DTOs, Stage 2 session 13): only Shared.Contracts, no package.
        Only("DirectService.Platform.Contracts", r => r == SharedContracts, "Platform.Contracts references only Shared.Contracts");
        var file = Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "src", "Platform", "DirectService.Platform.Contracts"), "*.csproj").Single();
        Assert.Empty(XDocument.Load(file).Descendants("PackageReference"));
        var compiled = typeof(DirectService.Platform.Contracts.Identity.MeResponse).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n != "netstandard" && n != "mscorlib" && n != SharedContracts).ToList();
        Assert.True(compiled.Count == 0, "The compiled Platform.Contracts references " + string.Join(", ", compiled));
        Assert.Contains("DirectService.Platform.Contracts", Projects["DirectService.Transportation.Web.Client"]);
    }

    [Fact]
    public void The_browser_app_does_not_receive_SharedKernel_through_the_contracts()
    {
        // Transportation.Contracts uses only the constants of SharedErrorCodes; its reference is private (DT-310).
        var contracts = XDocument.Load(Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "src", "DirectService.Transportation.Contracts"), "*.csproj").Single());
        var kernel = contracts.Descendants("ProjectReference").Single(r => r.Attribute("Include")!.Value.Contains(Kernel, StringComparison.Ordinal));
        Assert.Equal("all", kernel.Attribute("PrivateAssets")?.Value);
        var compiled = typeof(DirectService.Transportation.Contracts.Errors.ErrorCodes).Assembly.GetReferencedAssemblies().Select(a => a.Name!);
        Assert.DoesNotContain(Kernel, compiled);
    }

    [Fact]
    public void Shared_projects_reference_no_platform_or_module_project()
    {
        foreach (var p in Projects.Keys.Where(p => p.StartsWith("DirectService.Shared", StringComparison.Ordinal)))
            Only(p, r => r.StartsWith("DirectService.Shared", StringComparison.Ordinal), "Shared.* references no Platform or module project");
    }

    [Fact]
    public void Domain_projects_reference_only_SharedKernel()
    {
        // A module's Domain may also reference Platform.Domain, for the anchor types only (exception E-1, checked below).
        var domains = Projects.Keys.Where(p => p.EndsWith(".Domain", StringComparison.Ordinal)).ToList();
        Assert.Contains("DirectService.Transportation.Domain", domains);
        Assert.Contains(PlatformDomain, domains);
        foreach (var p in domains)
            Only(p, r => r == Kernel || (r == PlatformDomain && p != PlatformDomain), "a Domain project references SharedKernel only (a module's also Platform.Domain, E-1)");

        var compiled = typeof(DirectService.Transportation.Domain.Trn.Trip).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("DirectService.", StringComparison.Ordinal)).ToList();
        Assert.True(compiled.All(n => n is Kernel or PlatformDomain), "The compiled Transportation domain references " + string.Join(", ", compiled));
        compiled = typeof(DirectService.Platform.Domain.User).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("DirectService.", StringComparison.Ordinal)).ToList();
        Assert.True(compiled.All(n => n == Kernel), "The compiled Platform domain references " + string.Join(", ", compiled));
    }

    private const string PlatformDomain = "DirectService.Platform.Domain";

    /// <summary>The Platform types a module may use: the anchors of exception E-1 (K-09), for foreign-key mapping only.</summary>
    private static readonly string[] Anchors = ["Tenant", "Company", "User"];

    /// <summary>Every Platform.Domain type the compiled assembly refers to (its type references into that assembly).</summary>
    private static List<string> PlatformTypesUsedBy(Assembly assembly) => TypesUsedBy(assembly, PlatformDomain);

    /// <summary>Every type of <paramref name="target"/> the compiled assembly refers to (its type references into that assembly).</summary>
    private static List<string> TypesUsedBy(Assembly assembly, string target)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        return md.TypeReferences.Select(md.GetTypeReference)
            .Where(t => t.ResolutionScope.Kind == System.Reflection.Metadata.HandleKind.AssemblyReference
                && md.GetString(md.GetAssemblyReference((System.Reflection.Metadata.AssemblyReferenceHandle)t.ResolutionScope).Name) == target)
            .Select(t => md.GetString(t.Namespace) + "." + md.GetString(t.Name)).Distinct().Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void Platform_infrastructure_references_its_application_and_shared_infrastructure()
    {
        // Design a.2, without exception since Stage 2 session 12 (the temporary Transportation.Application reference of DT-339 is gone).
        Only("DirectService.Platform.Infrastructure",
            r => r is "DirectService.Platform.Application" or "DirectService.Shared.Infrastructure",
            "Platform.Infrastructure references its own Application and Shared.Infrastructure");
        Only("DirectService.Platform.Application", r => r is PlatformDomain or "DirectService.Platform.Contracts" or Kernel,
            "Platform.Application references its own Domain and Contracts and SharedKernel");
        foreach (var assembly in new[] { typeof(DirectService.Platform.Infrastructure.DependencyInjection).Assembly, typeof(DirectService.Platform.Application.DependencyInjection).Assembly })
        {
            var modules = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("DirectService.Transportation.", StringComparison.Ordinal)).ToList();
            Assert.True(modules.Count == 0, $"The compiled {assembly.GetName().Name} references " + string.Join(", ", modules));
        }
    }

    [Fact]
    public void Module_application_references_its_domain_and_contracts_and_the_platform_contracts()
    {
        // Design a.2. The one temporary exception (Stage 2 session 12, DT-343): Platform.Application. Since session 13 the identity, tenant,
        // console, subscription, staff and onboarding services are in Platform.Application; what the module still uses of it is pinned below
        // (the notification, audit-log and company-settings services still here, and the module's calls into platform services), so the
        // reference can only shrink until it goes.
        Only("DirectService.Transportation.Application",
            r => r is "DirectService.Transportation.Domain" or "DirectService.Transportation.Contracts" or "DirectService.Platform.Contracts" or Kernel
                or "DirectService.Platform.Application",
            "a module's Application references its own Domain and Contracts, SharedKernel and Platform.Contracts");
        var used = TypesUsedBy(typeof(DirectService.Transportation.Application.DependencyInjection).Assembly, "DirectService.Platform.Application");
        var more = used.Except(StillUsedOfPlatformApplication, StringComparer.Ordinal).ToList();
        Assert.True(more.Count == 0, "Transportation.Application uses more of Platform.Application (DT-343): " + string.Join(", ", more));
    }

    /// <summary>The Platform.Application types Transportation.Application may still use (DT-343, end of Stage 2 session 13); remove entries as they go.</summary>
    private static readonly string[] StillUsedOfPlatformApplication =
    [
        "DirectService.Platform.Application.Billing.PlanLimits",                    // TripService: trips per month (plan limit)
        "DirectService.Platform.Application.Companies.CompanyInfo",                 // CompanyHeaderPrinting
        "DirectService.Platform.Application.Identity.AccessService",                // NotificationLinks: tenant screens
        "DirectService.Platform.Application.Identity.AccountService",               // CompanySettingsService, CustomerService: e-mail checks
        "DirectService.Platform.Application.Identity.GrantGuard",                   // PersonalDataService
        "DirectService.Platform.Application.Notifications.IUserNotifier",           // NotificationService implements it
        "DirectService.Platform.Application.Notifications.PlatformNotifications",   // NotificationService / NotificationTexts
        "DirectService.Platform.Application.Tenants.CompanyExportService",          // TransportationExports: the "company" export kind
    ];

    [Fact]
    public void Modules_use_only_the_platform_anchor_types_for_foreign_keys_E1()
    {
        // Exception E-1 (design a.2, owner decision K-09): a module's Domain and Infrastructure may use Tenant, Company and User of
        // Platform.Domain, and only to map foreign keys. Transportation.Application still holds Platform services (console, tenants,
        // subscriptions, account and user services; DT-343), so it is checked once they have moved to Platform.Application.
        var domain = typeof(DirectService.Transportation.Domain.Trn.Trip).Assembly;
        var infrastructure = typeof(DirectService.Transportation.Infrastructure.Persistence.TransportationModelContributor).Assembly;
        foreach (var assembly in new[] { domain, infrastructure })
        {
            var used = PlatformTypesUsedBy(assembly);
            Assert.NotEmpty(used);   // the reader works: the User navigations and their mapping
            var others = used.Except(Anchors.Select(a => PlatformDomain + "." + a)).ToList();
            Assert.True(others.Count == 0, $"{assembly.GetName().Name} uses Platform types other than the anchors (E-1): " + string.Join(", ", others));
        }
        // Nor any other Platform project but the contracts (the permission rows it seeds).
        var platformProjects = infrastructure.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("DirectService.Platform.", StringComparison.Ordinal) && n is not PlatformDomain and not "DirectService.Platform.Contracts").ToList();
        Assert.True(platformProjects.Count == 0, "Transportation.Infrastructure references " + string.Join(", ", platformProjects));

        // In a module's Domain an anchor appears only as a navigation property of an entity, mapped to a foreign key in the model.
        var model = Golden.Model;
        foreach (var type in domain.GetTypes())
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(p => p.PropertyType.Assembly.GetName().Name == PlatformDomain))
            {
                var navigation = model.FindEntityType(type)?.FindNavigation(p.Name);
                Assert.True(navigation is { IsCollection: false } && navigation.ForeignKey.PrincipalEntityType.ClrType == p.PropertyType,
                    $"{type.Name}.{p.Name} uses a Platform type but is not a foreign-key navigation (E-1)");
            }
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName))
            {
                var platform = m.GetParameters().Select(x => x.ParameterType).Append(m.ReturnType).Where(t => t.Assembly.GetName().Name == PlatformDomain).ToList();
                Assert.True(platform.Count == 0, $"{type.Name}.{m.Name} uses the Platform type {platform.FirstOrDefault()?.Name} (E-1 allows foreign-key navigations only)");
            }
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => f.FieldType.Assembly.GetName().Name == PlatformDomain && !f.Name.Contains("k__BackingField", StringComparison.Ordinal)).ToList();
            Assert.True(fields.Count == 0, $"{type.Name} has a field of a Platform type (E-1 allows foreign-key navigations only)");
        }
    }

    [Fact]
    public void The_kernel_validation_code_equals_the_error_catalog()
    {
        // AppException moved to SharedKernel without the error catalog of Contracts; its default code must stay the catalog's.
        Assert.Equal(DirectService.Transportation.Contracts.Errors.ErrorCodes.ValidationFailed, AppException.ValidationFailedCode);
        Assert.Equal(AppException.ValidationFailedCode, new AppException().Code);
    }

    [Fact]
    public void Every_shared_error_code_is_in_the_error_catalog_under_the_same_name()
    {
        // DT-310: each shared code is defined once (SharedErrorCodes) and ErrorCodes names it; the catalogue holds both messages.
        var catalog = typeof(DirectService.Transportation.Contracts.Errors.ErrorCodes);
        foreach (var f in typeof(SharedErrorCodes).GetFields().Where(f => f.IsLiteral))
        {
            var value = (string)f.GetValue(null)!;
            Assert.Equal(value, (string?)catalog.GetField(f.Name)?.GetValue(null));
            Assert.True(DirectService.Transportation.Contracts.Errors.ErrorCatalog.IsKnown(value), $"{value} has no messages");
        }
    }

    [Fact]
    public void Every_platform_error_code_is_in_the_error_catalog_under_the_same_name()
    {
        // Stage 2 session 13 (the DT-310 pattern): each platform code is defined once (PlatformErrorCodes) and ErrorCodes names it; the
        // catalogue holds both messages; no value is defined twice.
        var catalog = typeof(DirectService.Transportation.Contracts.Errors.ErrorCodes);
        var platform = typeof(DirectService.Platform.Contracts.Errors.PlatformErrorCodes).GetFields().Where(f => f.IsLiteral).ToList();
        Assert.NotEmpty(platform);
        foreach (var f in platform)
        {
            var value = (string)f.GetValue(null)!;
            Assert.Equal(value, (string?)catalog.GetField(f.Name)?.GetValue(null));
            Assert.True(DirectService.Transportation.Contracts.Errors.ErrorCatalog.IsKnown(value), $"{value} has no messages");
        }
        var shared = typeof(SharedErrorCodes).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetValue(null)!).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(platform, f => shared.Contains((string)f.GetValue(null)!));
    }

    [Fact]
    public void The_platform_permission_keys_and_preset_roles_are_named_by_the_module_under_the_same_names()
    {
        // Stage 2 session 13: core.* keys and the preset role names are defined in Platform.Contracts; PermissionKeys / PresetRoles name them,
        // and the module's catalogue (IPermissionCatalog) gives every key and the keys of every preset role (values pinned, e.2).
        var keys = typeof(DirectService.Transportation.Contracts.Security.PermissionKeys);
        foreach (var f in typeof(DirectService.Platform.Contracts.Security.PlatformPermissionKeys).GetFields().Where(f => f.IsLiteral))
            Assert.Equal((string)f.GetValue(null)!, (string?)keys.GetField(f.Name)?.GetValue(null));
        var roles = typeof(DirectService.Transportation.Contracts.Security.PresetRoles);
        foreach (var f in typeof(DirectService.Platform.Contracts.Security.PlatformPresetRoles).GetFields().Where(f => f.IsLiteral))
            Assert.Equal((string)f.GetValue(null)!, (string?)roles.GetField(f.Name)?.GetValue(null));
        Assert.Equal(["Owner", "Administrator", "Accountant", "Dispatcher", "Viewer"], DirectService.Platform.Contracts.Security.PlatformPresetRoles.All);

        DirectService.Platform.Contracts.Modules.IPermissionCatalog[] catalogs = [new DirectService.Transportation.Application.Modules.TransportationPermissions()];
        Assert.Equal(DirectService.Transportation.Contracts.Security.PermissionKeys.All, DirectService.Platform.Application.Identity.PermissionCatalogs.AllKeys(catalogs));
        foreach (var role in DirectService.Platform.Contracts.Security.PlatformPresetRoles.All)
            Assert.Equal(DirectService.Transportation.Contracts.Security.PresetRoles.Permissions[role],
                DirectService.Platform.Application.Identity.PermissionCatalogs.PresetRoleKeys(catalogs, role));
    }
}
