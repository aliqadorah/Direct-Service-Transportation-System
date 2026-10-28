using System.Reflection;
using System.Xml.Linq;

namespace DirectService.Architecture.Tests;

/// <summary>
/// Layer and module rules of the platform design (a.2), first set (Stage 2 session 2, DT-290): SharedKernel references nothing of the
/// solution; Shared.* references no Platform or module project; a Domain project references SharedKernel only. Checked on the project files
/// (every project under src/, empty ones included) and on the compiled assemblies.
/// </summary>
public sealed class LayerRuleTests
{
    private const string Kernel = "DirectService.SharedKernel";

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
            Kernel, "DirectService.Shared.Infrastructure", "DirectService.Shared.UI",
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
    public void SharedKernel_references_no_project_of_the_solution()
    {
        Only(Kernel, _ => false, "SharedKernel references nothing of the solution");
        var compiled = typeof(StoredNames).Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("DirectService.", StringComparison.Ordinal)).ToList();
        Assert.True(compiled.Count == 0, "The compiled SharedKernel references " + string.Join(", ", compiled));
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
}
