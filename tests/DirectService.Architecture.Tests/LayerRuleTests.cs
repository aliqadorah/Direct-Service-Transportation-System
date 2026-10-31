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
    private const string SharedUi = "DirectService.Shared.UI";

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
        // The one exception (owner decision DT-367, Stage 2 session 15): Shared.UI may use the platform's browser-safe DTOs, because the shell
        // shows the signed-in user, the companies and the notifications. Platform.Contracts itself references only Shared.Contracts.
        foreach (var p in Projects.Keys.Where(p => p.StartsWith("DirectService.Shared", StringComparison.Ordinal)))
            Only(p, r => r.StartsWith("DirectService.Shared", StringComparison.Ordinal) || p == SharedUi && r == "DirectService.Platform.Contracts",
                "Shared.* references no Platform or module project (Shared.UI: Platform.Contracts only)");
    }

    [Fact]
    public void The_ui_libraries_reference_only_contracts_and_shared_ui()
    {
        // a.2: a UI library reaches data only through the REST API — Contracts and Shared.UI, never Application, Infrastructure or Domain.
        Only(SharedUi, r => r is SharedContracts or "DirectService.Platform.Contracts", "Shared.UI references Shared.Contracts and Platform.Contracts only");
        Only("DirectService.Platform.UI", r => r is SharedContracts or "DirectService.Platform.Contracts" or SharedUi,
            "Platform.UI references its own Contracts, Shared.Contracts and Shared.UI only");
    }

    [Fact]
    public void Domain_projects_reference_only_SharedKernel()
    {
        // A module's Domain may also reference Platform.Domain, for the anchor types only (exception E-1, checked below).
        var domains = Projects.Keys.Where(p => p.EndsWith(".Domain", StringComparison.Ordinal)).ToList();
        Assert.Contains("DirectService.Transportation.Domain", domains);
        Assert.Contains(AccountingDomain, domains);
        Assert.Contains(PlatformDomain, domains);
        foreach (var p in domains)
            Only(p, r => r == Kernel || (r == PlatformDomain && p != PlatformDomain), "a Domain project references SharedKernel only (a module's also Platform.Domain, E-1)");

        var compiled = typeof(DirectService.Transportation.Domain.Trn.Trip).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("DirectService.", StringComparison.Ordinal)).ToList();
        Assert.True(compiled.All(n => n is Kernel or PlatformDomain), "The compiled Transportation domain references " + string.Join(", ", compiled));
        compiled = typeof(DirectService.Accounting.Domain.Acc.JournalEntry).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("DirectService.", StringComparison.Ordinal)).ToList();
        Assert.True(compiled.All(n => n is Kernel or PlatformDomain), "The compiled Accounting domain references " + string.Join(", ", compiled));
        compiled = typeof(DirectService.Platform.Domain.User).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("DirectService.", StringComparison.Ordinal)).ToList();
        Assert.True(compiled.All(n => n == Kernel), "The compiled Platform domain references " + string.Join(", ", compiled));
    }

    private const string PlatformDomain = "DirectService.Platform.Domain";
    private const string AccountingDomain = "DirectService.Accounting.Domain";
    private const string AccountingApplication = "DirectService.Accounting.Application";

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
        // Design a.2, without exception since Stage 2 session 14 (the temporary Platform.Application reference of DT-343 is gone): the module
        // reaches the platform services through Platform.Contracts (notifier, plan limit, contribution interfaces) and other modules through their
        // Contracts (the accounting posting contract since Stage 3 session 2).
        Only("DirectService.Transportation.Application",
            r => r is "DirectService.Transportation.Domain" or "DirectService.Transportation.Contracts" or "DirectService.Platform.Contracts" or Kernel
                or "DirectService.Accounting.Contracts"
                or AccountingDomain        // TEMPORARY (Stage 3 session 3, DT-391): pinned by StillUsedOfAccountingDomain below
                or AccountingApplication,  // TEMPORARY (Stage 3 session 4a, DT-397): pinned by StillUsedOfAccountingApplication below
            "a module's Application references its own Domain and Contracts, SharedKernel, Platform.Contracts and other modules' Contracts");
        // The compiled assembly too (a reference could also come in through another project).
        var platform = typeof(DirectService.Transportation.Application.DependencyInjection).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("DirectService.Platform.", StringComparison.Ordinal) && n is not PlatformDomain and not "DirectService.Platform.Contracts").ToList();
        Assert.True(platform.Count == 0, "The compiled Transportation.Application references " + string.Join(", ", platform));
    }

    [Fact]
    public void Transportation_uses_only_the_pinned_accounting_entities_DT391()
    {
        // TEMPORARY (Stage 3 session 3, DT-391): Transportation.Application references Accounting.Domain until the transport code reaches
        // accounting only through Accounting.Contracts (Stage 3 session 16). The transport entities hold ids only (no accounting type), and the
        // module and the host may use exactly the accounting types listed: a new use fails, and so does a listed type no longer used, so the
        // list can only shrink.
        var domain = typeof(DirectService.Transportation.Domain.Trn.Trip).Assembly;
        Assert.Empty(TypesUsedBy(domain, AccountingDomain));
        var used = new[]
            {
                typeof(DirectService.Transportation.Application.DependencyInjection).Assembly,
                typeof(DirectService.Transportation.Infrastructure.Persistence.TransportationModelContributor).Assembly,
                typeof(DirectService.Transportation.Web.Security.AuthSchemes).Assembly,
            }
            .SelectMany(a => TypesUsedBy(a, AccountingDomain)).Distinct().Order(StringComparer.Ordinal).ToList();
        var more = used.Except(StillUsedOfAccountingDomain, StringComparer.Ordinal).ToList();
        Assert.True(more.Count == 0, "The transport code uses more of Accounting.Domain (DT-391): " + string.Join(", ", more));
        var gone = StillUsedOfAccountingDomain.Except(used, StringComparer.Ordinal).ToList();
        Assert.True(gone.Count == 0, "No longer used, remove from StillUsedOfAccountingDomain: " + string.Join(", ", gone));
    }

    /// <summary>The Accounting.Domain types the transport module and the host still use (DT-391, end of Stage 3 session 3); remove entries as they go.</summary>
    private static readonly string[] StillUsedOfAccountingDomain =
    [
        // all 23 at the start: the accounting services, endpoints and workers are still in the transport projects (sessions 4-15)
        // 22 after session 6c step 3 (journal export moved: JournalExportBatch)
        "DirectService.Accounting.Domain.Acc.Account",
        "DirectService.Accounting.Domain.Acc.ApArDocument",
        "DirectService.Accounting.Domain.Acc.ApArDocumentLine",
        "DirectService.Accounting.Domain.Acc.ApArEInvoiceSubmission",
        "DirectService.Accounting.Domain.Acc.ApArParty",
        "DirectService.Accounting.Domain.Acc.ApArPayment",
        "DirectService.Accounting.Domain.Acc.ApArReturn",
        "DirectService.Accounting.Domain.Acc.BankAccount",
        "DirectService.Accounting.Domain.Acc.Budget",
        "DirectService.Accounting.Domain.Acc.BudgetLine",
        "DirectService.Accounting.Domain.Acc.CashBox",
        "DirectService.Accounting.Domain.Acc.CostCentre",
        "DirectService.Accounting.Domain.Acc.EntryLine",
        "DirectService.Accounting.Domain.Acc.JournalDraft",
        "DirectService.Accounting.Domain.Acc.JournalEntry",
        "DirectService.Accounting.Domain.Acc.JournalTemplate",
        "DirectService.Accounting.Domain.Acc.PendingPosting",
        "DirectService.Accounting.Domain.Acc.Period",
        "DirectService.Accounting.Domain.Acc.PostingKeyMapping",
        "DirectService.Accounting.Domain.Acc.TaxCode",
        "DirectService.Accounting.Domain.Cfg.EInvoiceSettings",
        "DirectService.Accounting.Domain.Core.MaintenanceLog",
    ];

    [Fact]
    public void Transportation_uses_only_the_pinned_accounting_services_DT397()
    {
        // TEMPORARY (Stage 3 session 4a, DT-397): Transportation.Application references Accounting.Application while the documents still call
        // the posting engine (sessions 4b-5) and the accounting services are in the transport projects (sessions 6-12). The transport domain
        // uses nothing of it; the transport application and infrastructure use exactly the types listed: a new use fails, and so does a listed
        // type no longer used, so the list can only shrink. The host is the composition root (design a.2) and is not pinned.
        Assert.Empty(TypesUsedBy(typeof(DirectService.Transportation.Domain.Trn.Trip).Assembly, AccountingApplication));
        var used = new[]
            {
                typeof(DirectService.Transportation.Application.DependencyInjection).Assembly,
                typeof(DirectService.Transportation.Infrastructure.Persistence.TransportationModelContributor).Assembly,
            }
            .SelectMany(a => TypesUsedBy(a, AccountingApplication)).Distinct().Order(StringComparer.Ordinal).ToList();
        var more = used.Except(StillUsedOfAccountingApplication, StringComparer.Ordinal).ToList();
        Assert.True(more.Count == 0, "The transport code uses more of Accounting.Application (DT-397): " + string.Join(", ", more));
        var gone = StillUsedOfAccountingApplication.Except(used, StringComparer.Ordinal).ToList();
        Assert.True(gone.Count == 0, "No longer used, remove from StillUsedOfAccountingApplication: " + string.Join(", ", gone));
    }

    /// <summary>The Accounting.Application types the transport module still uses (DT-397, end of Stage 3 session 4b); remove entries as they go.</summary>
    private static readonly string[] StillUsedOfAccountingApplication =
    [
        // 10 at the start (session 4a); 9 after session 4b (trips, invoices, credit notes and the pending-posting retry use the contract):
        // the other documents still post through the engine (session 5), the accounting services use its codes, keys and helpers (6-12)
        // 7 after session 6c step 3 (the journal services moved: PostingLine, PostingRequest)
        "DirectService.Accounting.Application.Posting.AccountingCodes",
        "DirectService.Accounting.Application.Posting.AccountingPostingKeys",
        "DirectService.Accounting.Application.Posting.IPeriodGuard",
        "DirectService.Accounting.Application.Posting.IPostingKeyCatalog",
        "DirectService.Accounting.Application.Posting.JournalTax",
        "DirectService.Accounting.Application.Posting.PostingEngine",
        "DirectService.Accounting.Application.Posting.PostingParty",
    ];

    [Fact]
    public void Modules_use_only_the_platform_anchor_types_for_foreign_keys_E1()
    {
        // Exception E-1 (design a.2, owner decision K-09): a module's Domain, Application and Infrastructure may use Tenant, Company and
        // User of Platform.Domain, and only to map foreign keys and read those rows (Application checked since Stage 2 session 14, when the
        // Platform services it held had moved).
        var domain = typeof(DirectService.Transportation.Domain.Trn.Trip).Assembly;
        var application = typeof(DirectService.Transportation.Application.DependencyInjection).Assembly;
        var infrastructure = typeof(DirectService.Transportation.Infrastructure.Persistence.TransportationModelContributor).Assembly;
        var accountingDomain = typeof(DirectService.Accounting.Domain.Acc.JournalEntry).Assembly;
        var accountingApplication = typeof(DirectService.Accounting.Application.DependencyInjection).Assembly;
        var accountingInfrastructure = typeof(DirectService.Accounting.Infrastructure.Persistence.AccountingModelContributor).Assembly;
        foreach (var assembly in new[] { domain, application, infrastructure, accountingDomain, accountingApplication, accountingInfrastructure })
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
        foreach (var type in domain.GetTypes().Concat(accountingDomain.GetTypes()))
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
    public void Every_accounting_error_code_is_in_the_error_catalog_under_the_same_name()
    {
        // Stage 3 session 4a (DT-396, the DT-310 pattern): the posting engine's codes are defined once (AccountingErrorCodes) and ErrorCodes names
        // them; the catalogue holds both messages; no value is also a shared or platform code. Likewise the permission key and notification type
        // the engine uses.
        var catalog = typeof(DirectService.Transportation.Contracts.Errors.ErrorCodes);
        var accounting = typeof(DirectService.Accounting.Contracts.Errors.AccountingErrorCodes).GetFields().Where(f => f.IsLiteral).ToList();
        Assert.Equal(34, accounting.Count);   // 19 of the engine (session 4a) + 15 of the services of session 6
        foreach (var f in accounting)
        {
            var value = (string)f.GetValue(null)!;
            Assert.Equal(value, (string?)catalog.GetField(f.Name)?.GetValue(null));
            Assert.True(DirectService.Transportation.Contracts.Errors.ErrorCatalog.IsKnown(value), $"{value} has no messages");
        }
        var others = typeof(SharedErrorCodes).GetFields().Concat(typeof(DirectService.Platform.Contracts.Errors.PlatformErrorCodes).GetFields())
            .Where(f => f.IsLiteral).Select(f => (string)f.GetValue(null)!).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(accounting, f => others.Contains((string)f.GetValue(null)!));
        Assert.Equal(DirectService.Accounting.Contracts.Security.AccountingPermissionKeys.JeExport, DirectService.Transportation.Contracts.Security.PermissionKeys.JeExport);
        Assert.Equal(DirectService.Accounting.Contracts.Security.AccountingNotificationTypes.PostingFailed,
            DirectService.Transportation.Application.Collaboration.TransportationNotificationTypes.PostingFailed);
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
