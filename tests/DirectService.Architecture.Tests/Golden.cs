using System.Reflection;
using System.Text.Json.Nodes;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Database;
using DirectService.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DirectService.Architecture.Tests;

/// <summary>
/// The golden files (<c>Golden/*.json</c>): every name and key stored in data or used as a key, generated once from the restore tag
/// <c>pre-modules-final-2026-10-27</c> by running that tag's own code in a temporary worktree (BUILD_STATUS, DT-283). The tests compute the
/// same values from the current code; a difference means a stored value changed, which breaks existing data, links or sign-ins. Fix the
/// code, never the golden file.
/// </summary>
internal static class Golden
{
    public const string FixedTokenKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    public static JsonNode Load(string file) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", file)))
        ?? throw new InvalidOperationException("Golden file " + file + " is empty.");

    public static JsonNode Entities => Load("entities.json");

    public static JsonNode Keys => Load("stored-keys.json");

    public static JsonNode Security => Load("security-and-browser.json");

    public static JsonNode OpenApi => Load("openapi.json");

    public static IReadOnlyList<string> Strings(JsonNode? array) =>
        array?.AsArray().Select(v => v!.GetValue<string>()).ToList() ?? [];

    public static Dictionary<string, string> Map(JsonNode? obj) =>
        obj?.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>(), StringComparer.Ordinal) ?? new(StringComparer.Ordinal);

    /// <summary>The repository root (the folder holding the solution file), for the checks that read sources.</summary>
    public static string RepoRoot
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
                if (d.GetFiles("*.slnx").Length > 0) return d.FullName;
            throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
        }
    }

    /// <summary>The EF model of the composed database context (built in memory; nothing connects to a database).</summary>
    public static IModel Model => ModelHolder.Value;

    private static readonly Lazy<IModel> ModelHolder = new(() =>
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        return db.Model;
    });

    /// <summary>The model of the migrations' snapshot (MasterDbContextModelSnapshot): the model as the last migration left it.</summary>
    public static IModel SnapshotModel => SnapshotHolder.Value;

    private static readonly Lazy<IModel> SnapshotHolder = new(() =>
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        return Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>(db)
            .ModelSnapshot!.Model;
    });

    /// <summary>Every application assembly (all modules), wherever a constant moves to.</summary>
    public static IReadOnlyList<Assembly> AppAssemblies =>
    [
        typeof(DirectService.Transportation.Application.DependencyInjection).Assembly,   // Application (DataScope is SharedKernel since Stage 2 session 13)
        typeof(DirectService.Platform.Application.DependencyInjection).Assembly,
        typeof(DirectService.Platform.Infrastructure.DependencyInjection).Assembly,
        typeof(DirectService.Platform.Contracts.Errors.PlatformErrorCodes).Assembly,
        typeof(DirectService.Transportation.Infrastructure.DependencyInjection).Assembly,
        typeof(MasterDbContext).Assembly,   // Shared.Infrastructure
        typeof(DirectService.Transportation.Web.Security.AuthSchemes).Assembly,
        typeof(DirectService.Transportation.Contracts.Security.PermissionKeys).Assembly,
        typeof(AuditableEntity).Assembly,   // SharedKernel
        typeof(DirectService.Transportation.Domain.Trn.Trip).Assembly,
    ];

    /// <summary>A type found by its simple name in the application assemblies (namespaces change in Stages 2–4, names do not).</summary>
    public static Type TypeNamed(string name) =>
        AppAssemblies.SelectMany(a => a.GetTypes()).SingleOrDefault(t => t.Name == name && !t.IsNested)
        ?? throw new InvalidOperationException("No type named " + name + " in the application assemblies.");

    /// <summary>The public string constants of a type, by field name.</summary>
    public static Dictionary<string, string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

    /// <summary>Sorted ordinal, for comparing sets in messages.</summary>
    public static List<string> Sorted(IEnumerable<string> values) => values.Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>Fails with the missing and extra values (and changed entries) when two sets of stored values differ.</summary>
    public static void SameSet(string what, IEnumerable<string> golden, IEnumerable<string> actual)
    {
        var g = Sorted(golden);
        var a = Sorted(actual);
        if (g.SequenceEqual(a)) return;
        Assert.Fail($"{what} changed — a stored value must stay as at the restore tag.\n  missing now: {string.Join(", ", g.Except(a))}\n  new/changed: {string.Join(", ", a.Except(g))}");
    }

    /// <summary>Fails when a golden value is gone (new values may be added, e.g. a new report field).</summary>
    public static void Kept(string what, IEnumerable<string> golden, IEnumerable<string> actual)
    {
        var gone = Sorted(golden.Except(actual, StringComparer.Ordinal));
        if (gone.Count > 0) Assert.Fail($"{what}: values of the restore tag are gone (renamed or removed): {string.Join(", ", gone)}");
    }

    /// <summary>Fails listing every key whose value differs, is missing or is new.</summary>
    public static void SameMap(string what, IReadOnlyDictionary<string, string> golden, IReadOnlyDictionary<string, string> actual)
    {
        var problems = golden.Where(p => !actual.TryGetValue(p.Key, out var v) || v != p.Value)
            .Select(p => $"{p.Key}: golden \"{p.Value}\", now {(actual.TryGetValue(p.Key, out var v) ? $"\"{v}\"" : "missing")}")
            .Concat(actual.Keys.Except(golden.Keys).Select(k => $"{k}: new \"{actual[k]}\" (not in the golden file)")).ToList();
        if (problems.Count > 0) Assert.Fail($"{what} changed — a stored value must stay as at the restore tag:\n  " + string.Join("\n  ", problems));
    }
}
