using System.Text.RegularExpressions;

namespace DirectService.Architecture.Tests;

/// <summary>
/// The GitHub workflow runs every test project the same way (DT-225, DT-287): Release, no build, the coverage options. A malformed command
/// (a stray argument such as a literal "\n" from an edit — the cause of the CI failure of 28 Oct 2026) makes the test host refuse its command
/// line: exit code 5, "zero tests ran". These checks catch it before a push.
/// </summary>
public sealed class CiWorkflowTests
{
    private static readonly string Workflow = File.ReadAllText(Path.Combine(Golden.RepoRoot, ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Every shell line of the workflow with its "\" continuations joined.</summary>
    private static IEnumerable<string> Commands() =>
        Regex.Replace(Workflow, "\\s*\\\\\\n\\s*", " ").Split('\n').Select(l => l.Trim());

    [Fact]
    public void No_command_line_carries_a_stray_backslash()
    {
        var bad = Commands().Where(l => !l.StartsWith('#') && Regex.IsMatch(l, "\\\\[A-Za-z]")).ToList();
        Assert.True(bad.Count == 0, "Workflow lines with a literal backslash sequence (a broken line continuation?):\n  " + string.Join("\n  ", bad));
    }

    [Fact]
    public void Every_test_project_runs_in_ci_with_the_same_coverage_options()
    {
        var tests = Directory.EnumerateFiles(Path.Combine(Golden.RepoRoot, "tests"), "*.Tests.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();
        Assert.NotEmpty(tests);

        // The projects named by the dotnet test commands, with "for p in A B; do … $p …" loops expanded.
        var run = new List<string>();
        string[]? loop = null;
        foreach (var line in Commands())
        {
            if (Regex.Match(line, "^for p in (.+); do$") is { Success: true } m) loop = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            else if (line == "done") loop = null;
            if (!line.StartsWith("dotnet test ", StringComparison.Ordinal)) continue;
            Assert.Matches("^dotnet test --project tests/\\S+ -c Release --no-build --coverage --coverage-output-format cobertura --coverage-output \\S+\\.cobertura\\.xml$", line);
            var project = Regex.Match(line, "--project tests/(\\S+)").Groups[1].Value;
            run.AddRange(project.Contains("$p", StringComparison.Ordinal) && loop is not null ? loop.Select(p => project.Replace("$p", p, StringComparison.Ordinal)) : [project]);
        }
        Golden.SameSet("Test projects run by ci.yml (every *.Tests project under tests/)", tests, run);
    }
}
