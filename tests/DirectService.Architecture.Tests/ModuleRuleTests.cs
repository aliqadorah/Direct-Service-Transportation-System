using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace DirectService.Architecture.Tests;

/// <summary>
/// The full table of design a.2 (Stage 2 session 16), for every project under src/ and every compiled assembly, whatever module it belongs to
/// (a new module's projects are checked the moment they exist). The rules are written without exceptions; the only references outside them
/// must be exactly the two decided ones: Shared.UI → Platform.Contracts (owner decision DT-367) and a module Domain → Platform.Domain for the
/// anchor types (E-1, K-09; what the module may use of it is checked by <see cref="LayerRuleTests"/>). The general rule of the start guide —
/// everyone may use Shared (of its own layer) and Platform.Contracts — is part of the table, not an exception. One temporary reference besides
/// them (Stage 3 session 3, DT-391, gone by session 16): Transportation.Application → Accounting.Domain, the accounting entities the transport
/// code still uses, each named by <see cref="LayerRuleTests"/> (StillUsedOfAccountingDomain); and a second (Stage 3 session 4a, DT-397):
/// Transportation.Application → Accounting.Application, the posting engine and its codes, named by StillUsedOfAccountingApplication. A module's
/// Contracts may use another module's Contracts (browser-safe both; DT-396, DT-407).
/// </summary>
public sealed class ModuleRuleTests
{
    private const string Prefix = "DirectService.";
    private const string Kernel = "DirectService.SharedKernel";
    private const string SharedContracts = "DirectService.Shared.Contracts";
    private const string SharedInfrastructure = "DirectService.Shared.Infrastructure";
    private const string SharedUi = "DirectService.Shared.UI";
    private const string PlatformContracts = "DirectService.Platform.Contracts";
    private const string PlatformDomain = "DirectService.Platform.Domain";
    private const string AccountingDomain = "DirectService.Accounting.Domain";
    private const string AccountingApplication = "DirectService.Accounting.Application";

    private static readonly string[] Layers = ["Domain", "Contracts", "Application", "Infrastructure", "UI"];

    private sealed record Reference(string Name, bool Private);

    /// <summary>Every project under src/ with its project references (and whether each is private to it).</summary>
    private static readonly Dictionary<string, List<Reference>> Projects =
        Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => XDocument.Load(f).Descendants("ProjectReference")
                .Select(r => new Reference(Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)),
                    string.Equals(r.Attribute("PrivateAssets")?.Value, "all", StringComparison.OrdinalIgnoreCase))).ToList(),
                StringComparer.Ordinal);

    /// <summary>The kind of a project: Kernel, Shared, a layer of an owner (Platform or a module), or a host.</summary>
    private sealed record Kind(string Owner, string Layer)
    {
        public bool IsModule => Owner is not ("Shared" or "Platform" or "Host");
    }

    private static Kind KindOf(string project)
    {
        if (project == Kernel) return new("Shared", "Kernel");
        if (project is "DirectService.Database" || project.EndsWith(".Web", StringComparison.Ordinal)) return new("Host", "Server");
        if (project.EndsWith(".Web.Client", StringComparison.Ordinal)) return new("Host", "Browser");
        var parts = project[Prefix.Length..].Split('.');
        Assert.True(parts.Length == 2 && Layers.Contains(parts[1]), $"{project}: not a known project kind of design a.1 (owner.layer)");
        return new(parts[0], parts[1]);
    }

    /// <summary>Design a.2, written without any exception.</summary>
    private static bool Allowed(string project, string reference)
    {
        var p = KindOf(project);
        var r = KindOf(reference);
        if (p.Owner == "Host")
            return p.Layer == "Browser"
                ? r.Layer is "UI" or "Contracts"                                     // Web.Client: the UI projects, Shared.UI, Contracts
                : project.EndsWith(".Web", StringComparison.Ordinal)                 // Web: everything (composition root)
                  || r.Layer == "Infrastructure";                                    // Database (K-08): the model contributors
        if (p.Owner == "Shared")
            return p.Layer switch
            {
                "Kernel" => reference == SharedContracts,                              // DT-309
                "Contracts" => false,                                                  // browser-safe, nothing at all
                "Infrastructure" => reference is Kernel or SharedContracts,
                _ => reference == SharedContracts,                                     // Shared.UI
            };
        var own = Prefix + p.Owner + ".";
        var common = reference is Kernel or SharedContracts || reference == PlatformContracts && p.Owner != "Platform";
        return p.Layer switch
        {
            "Domain" => reference == Kernel,
            "Contracts" => reference == SharedContracts || reference == PlatformContracts && p.Owner != "Platform"
                || reference == Kernel && Projects[project].Single(x => x.Name == Kernel).Private        // WASM-safe (DT-310)
                || r.Layer == "Contracts" && r.IsModule && r.Owner != p.Owner && p.IsModule,           // another module's Contracts (DT-396, DT-407)
            "Application" => reference == own + "Domain" || reference == own + "Contracts" || common
                || r.Layer == "Contracts" && r.IsModule,                                               // other modules' Contracts
            "Infrastructure" => reference == own + "Application" || reference == SharedInfrastructure || common,
            _ => reference == own + "Contracts" || reference is SharedUi or SharedContracts or PlatformContracts,   // UI
        };
    }

    /// <summary>The decided exceptions to the table: DT-367 and E-1, nothing else.</summary>
    private static bool IsDecidedException(string project, string reference) =>
        project == SharedUi && reference == PlatformContracts
        || reference == PlatformDomain && KindOf(project) is { Layer: "Domain", IsModule: true };

    /// <summary>
    /// TEMPORARY: the transport code still uses accounting entities until session 16 (Stage 3 session 3, DT-391) and the posting engine and
    /// its codes until the documents post through the contract and the accounting services have moved (Stage 3 session 4a, DT-397).
    /// </summary>
    private static bool IsTemporaryAccountingDomainReference(string project, string reference) =>
        project == "DirectService.Transportation.Application" && reference is AccountingDomain or AccountingApplication;

    [Fact]
    public void Every_project_reference_follows_design_a2_except_DT367_and_E1()
    {
        var outside = Projects.SelectMany(p => p.Value.Where(r => !Allowed(p.Key, r.Name)).Select(r => (Project: p.Key, Reference: r.Name))).ToList();
        var undecided = outside.Where(x => !IsDecidedException(x.Project, x.Reference) && !IsTemporaryAccountingDomainReference(x.Project, x.Reference))
            .Select(x => $"{x.Project} → {x.Reference}").ToList();
        Assert.True(undecided.Count == 0, "References outside design a.2: " + string.Join("; ", undecided));
        // Both exceptions are in use today (the test would otherwise not notice that one silently became a different reference).
        Assert.Contains((SharedUi, PlatformContracts), outside);
        Assert.Contains(("DirectService.Transportation.Domain", PlatformDomain), outside);
    }

    [Fact]
    public void Every_compiled_assembly_references_only_what_its_project_may_reach()
    {
        // A reference can also come in through a package or a hint path: the compiled assembly may name only projects it reaches through its
        // checked project references (a private reference does not flow on to the projects that use it).
        foreach (var project in Projects.Keys)
        {
            var reachable = Reach(project);
            var compiled = CompiledReferences(project).Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
            var bad = compiled.Except(reachable).ToList();
            Assert.True(bad.Count == 0, $"The compiled {project} references {string.Join(", ", bad)}, which its project references do not reach");
        }
    }

    [Fact]
    public void No_module_references_another_modules_domain_application_infrastructure_or_ui()
    {
        // a.2: a module uses another module only through its Contracts; checked on the projects and on what each compiled assembly reaches.
        foreach (var project in Projects.Keys.Where(p => KindOf(p).IsModule))
        {
            var owner = KindOf(project).Owner;
            var foreign = Reach(project).Where(r => r != project && KindOf(r) is { IsModule: true } k && k.Owner != owner && k.Layer != "Contracts")
                .Where(r => !(owner == "Transportation" && r is AccountingDomain or AccountingApplication
                    && Reach(project).Contains("DirectService.Transportation.Application")))   // DT-391, DT-397, temporary
                .ToList();
            Assert.True(foreign.Count == 0, $"{project} reaches another module's {string.Join(", ", foreign)}");
        }
    }

    [Fact]
    public void The_browser_assemblies_carry_no_server_code()
    {
        // The browser app talks only to the REST API: Contracts, UI libraries and the browser app reference no SharedKernel, Domain,
        // Application or Infrastructure assembly and no EF Core / ASP.NET Core server package (compiled references).
        var browser = Projects.Keys.Where(p => KindOf(p) is { Layer: "Contracts" or "UI" } or { Owner: "Host", Layer: "Browser" }).ToList();
        Assert.Contains("DirectService.Transportation.Web.Client", browser);
        foreach (var project in browser)
        {
            var bad = CompiledReferences(project).Where(n => n == Kernel || n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || n is "Microsoft.AspNetCore" or "Microsoft.AspNetCore.Http" or "Microsoft.AspNetCore.Identity"
                || n.StartsWith(Prefix, StringComparison.Ordinal) && KindOf(n).Layer is "Domain" or "Application" or "Infrastructure" or "Server").ToList();
            Assert.True(bad.Count == 0, $"The browser-side {project} references {string.Join(", ", bad)}");
        }
    }

    /// <summary>The projects the given project reaches through its references (itself included); private references stop there.</summary>
    private static HashSet<string> Reach(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { project };
        var queue = new Queue<string>(Projects[project].Select(r => r.Name));
        foreach (var r in Projects[project]) seen.Add(r.Name);
        while (queue.Count > 0)
            foreach (var r in Projects[queue.Dequeue()].Where(r => !r.Private && seen.Add(r.Name)))
                queue.Enqueue(r.Name);
        return seen;
    }

    /// <summary>The assembly names the compiled project references (read from its metadata, nothing loaded).</summary>
    private static List<string> CompiledReferences(string project)
    {
        var file = Path.Combine(AppContext.BaseDirectory, project + ".dll");
        Assert.True(File.Exists(file), $"{project}.dll is not next to the tests (the host references every project)");
        using var stream = File.OpenRead(file);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        return md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
    }
}
