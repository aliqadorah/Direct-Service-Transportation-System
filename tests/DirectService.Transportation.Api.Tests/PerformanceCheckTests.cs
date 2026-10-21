using System.Reflection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner testing 21 Oct 2026, item B (DT-227): the 500,000-trip timing test (NFR-01, 1 second) is a manual performance check. It is explicit,
/// so the normal <c>dotnet test</c> run and CI leave it out; <c>tools/perf/run-performance-check.*</c> runs it alone with
/// <c>--explicit only</c> on its own database. The limit is not changed.
/// </summary>
public sealed class PerformanceCheckTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DirectService.Transportation.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void The_500000_trip_test_is_explicit_in_the_performance_category_and_keeps_its_one_second_limit()
    {
        var method = typeof(TripPerformanceTests).GetMethod(nameof(TripPerformanceTests.Created_trips_list_with_500000_trips_answers_under_one_second_NFR01))!;
        Assert.True(method.GetCustomAttribute<FactAttribute>()!.Explicit);
        Assert.Contains(method.GetCustomAttributes<TraitAttribute>(), t => t.Name == "Category" && t.Value == "Performance");
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "DirectService.Transportation.Api.Tests", "TripPerformanceTests.cs"));
        Assert.Contains("ms < 1000", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_never_runs_explicit_tests_and_the_scripts_run_only_them_on_their_own_database()
    {
        var root = RepoRoot();
        Assert.DoesNotContain("--explicit", File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml")), StringComparison.Ordinal);
        foreach (var script in new[] { "run-performance-check.ps1", "run-performance-check.sh" })
        {
            var text = File.ReadAllText(Path.Combine(root, "tools", "perf", script));
            Assert.Contains("--explicit only", text, StringComparison.Ordinal);
            Assert.Contains("DirectServiceMaster_PerfTests", text, StringComparison.Ordinal);
            Assert.Contains("DST_API_TEST_DB", text, StringComparison.Ordinal);
        }
    }
}
