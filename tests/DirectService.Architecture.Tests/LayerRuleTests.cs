using System.Reflection;
using System.Xml.Linq;

namespace DirectService.Architecture.Tests;

/// <summary>
/// Layer and module rules of the platform design (a.2), first set (Stage 2 session 2, DT-290): SharedKernel references only the browser-safe
/// Shared.Contracts (DT-309); Shared.* references no Platform or module project; a Domain project references SharedKernel only. Checked on the project files
/// (every project under src/, empty ones included) and on the compiled assemblies.
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
        var domains = Projects.Keys.Where(p => p.EndsWith(".Domain", StringComparison.Ordinal)).ToList();
        Assert.Contains("DirectService.Transportation.Domain", domains);
        Assert.Contains("DirectService.Platform.Domain", domains);
        foreach (var p in domains) Only(p, r => r == Kernel, "a Domain project references SharedKernel only");

        // The compiled Transportation domain as well (Platform.Domain is still empty).
        var compiled = typeof(DirectService.Transportation.Domain.Trn.Trip).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
            .Where(n => n.StartsWith("DirectService.", StringComparison.Ordinal)).ToList();
        Assert.True(compiled.All(n => n == Kernel), "The compiled Transportation domain references " + string.Join(", ", compiled));
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
}
