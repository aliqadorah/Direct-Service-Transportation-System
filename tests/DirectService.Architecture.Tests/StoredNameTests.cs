using System.Reflection;
using System.Text.Json.Nodes;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Domain.Common;
using DirectService.Database;
using DirectService.Shared.Infrastructure.Persistence;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Shared.Infrastructure.Persistence.Interceptors;
using DirectService.Transportation.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace DirectService.Architecture.Tests;

/// <summary>
/// Entity stored names and URL token labels (platform design §e, E-1 … E-6): every entity carries explicit pins, and the code paths that write
/// or read them (audit interceptor, entity resolver, audit history, URL tokens, company export, tenant deletion) give exactly the values of
/// the restore tag. A moved or renamed class keeps passing as long as its pins are copied with it.
/// </summary>
public sealed class StoredNameTests
{
    private static readonly IReadOnlyList<Type> ModelTypes = EntityTypeMap.EntityTypes(Golden.Model);

    private static readonly Dictionary<string, JsonNode> GoldenByName =
        Golden.Entities["entities"]!.AsArray().ToDictionary(e => e!["storedName"]!.GetValue<string>(), e => e!, StringComparer.Ordinal);

    private static IEnumerable<string> GoldenWhere(string flag) =>
        GoldenByName.Where(p => p.Value[flag]!.GetValue<bool>()).Select(p => p.Key);

    [Fact]
    public void Every_entity_has_an_explicit_pinned_name_and_token_label_and_none_is_shared()
    {
        var missing = ModelTypes.Where(t => t.GetCustomAttribute<StoredNameAttribute>(false) is null || t.GetCustomAttribute<TokenLabelAttribute>(false) is null)
            .Select(t => t.FullName!).ToList();
        Assert.True(missing.Count == 0, "Entities without [StoredName] / [TokenLabel] (design §e, E-1/E-3): " + string.Join(", ", missing));

        // Every entity class of the domain is in the EF model, so the model-driven maps miss none.
        var domainEntities = Golden.AppAssemblies.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && StoredNames.IsPinned(t));
        Assert.Empty(domainEntities.Except(ModelTypes).Select(t => t.FullName));

        foreach (var (what, of) in new (string, Func<Type, string>)[] { ("stored name", StoredNames.Of), ("token label", StoredNames.TokenLabelOf) })
        {
            var shared = ModelTypes.GroupBy(of, StringComparer.Ordinal).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.FullName))}").ToList();
            Assert.True(shared.Count == 0, $"Two entities share a {what}: " + string.Join("; ", shared));
        }
    }

    [Fact]
    public void The_audit_interceptor_writes_the_golden_stored_name_of_every_entity()
    {
        var actual = ModelTypes.Select(t => AuditSaveChangesInterceptor.EntityTypeName(Activator.CreateInstance(t)!));
        Golden.SameSet("Entity stored names (core.AuditLogs.EntityType, E-1)", GoldenByName.Keys, actual);
    }

    [Fact]
    public void Url_tokens_of_every_entity_equal_the_tokens_of_the_restore_tag()
    {
        var file = Golden.Entities;
        var tokens = new UrlTokenService(Options.Create(new UrlTokenOptions { Key = file["tokenKey"]!.GetValue<string>() }));
        var guid = Guid.Parse(file["tokenGuid"]!.GetValue<string>());
        var problems = new List<string>();
        foreach (var t in ModelTypes)
        {
            var name = StoredNames.Of(t);
            if (!GoldenByName.TryGetValue(name, out var g)) { problems.Add($"{name}: not in the golden file"); continue; }
            if (StoredNames.TokenLabelOf(t) != g["tokenLabel"]!.GetValue<string>())
                problems.Add($"{name}: token label \"{StoredNames.TokenLabelOf(t)}\", golden \"{g["tokenLabel"]}\"");
            var token = tokens.Encode(t, guid);
            if (token != g["token"]!.GetValue<string>()) problems.Add($"{name}: token {token}, golden {g["token"]} — every saved link of this type would break");
            if (tokens.Decode(t, token) != guid) problems.Add($"{name}: the token does not decode back");
        }
        Assert.True(problems.Count == 0, "URL tokens changed (E-3):\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void Entity_resolver_record_locks_and_live_refresh_know_the_golden_names()
    {
        Golden.SameSet("Entity resolver names (record locks, E-2)", GoldenWhere("entityResolver"), EntityTypeMap.Records(Golden.Model).Keys);
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var resolver = new EntityResolver(db, new UrlTokenService(Options.Create(new UrlTokenOptions { Key = Golden.FixedTokenKey })));
        var unknown = GoldenWhere("entityResolver").Where(n => resolver.TypeOf(n) is null || StoredNames.Of(resolver.TypeOf(n)!) != n).ToList();
        Assert.True(unknown.Count == 0, "EntityResolver no longer resolves: " + string.Join(", ", unknown));
    }

    [Fact]
    public void Audit_history_resolves_the_golden_record_types()
    {
        Golden.SameSet("Audit history record types (AuditDescriber, E-2)", GoldenWhere("auditDescriber"), EntityTypeMap.WithLongId(Golden.Model).Keys);
    }

    [Fact]
    public void Company_export_and_tenant_deletion_cover_the_golden_types()
    {
        var export = EntityTypeMap.Records(Golden.Model).Values.GroupBy(StoredNames.ShortOf, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Key);
        var goldenExport = GoldenByName.Values.Select(e => e["exportRecordType"]?.GetValue<string>()).OfType<string>();
        Golden.SameSet("Company export record types (E-5)", goldenExport, export);

        var deleted = EntityTypeMap.Records(Golden.Model).Values
            .Where(t => t != typeof(DirectService.Transportation.Domain.Core.Tenant) && (typeof(ITenantScoped).IsAssignableFrom(t) || typeof(INullableTenantScoped).IsAssignableFrom(t)))
            .Select(StoredNames.Of);
        Golden.SameSet("Types soft-deleted with a tenant (E-2)", GoldenWhere("tenantDeletion"), deleted);
    }

    [Fact]
    public void No_entity_property_is_renamed()
    {
        // Audit JSON keys are property names (E-6): renaming one hides every old value from the history.
        var problems = new List<string>();
        foreach (var t in ModelTypes)
        {
            if (!GoldenByName.TryGetValue(StoredNames.Of(t), out var g)) continue;
            var golden = Golden.Strings(g["properties"]);
            var now = Golden.Sorted(t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name));
            if (!golden.SequenceEqual(now))
                problems.Add($"{StoredNames.Of(t)}: missing {string.Join(", ", golden.Except(now))}; new {string.Join(", ", now.Except(golden))}");
        }
        Assert.True(problems.Count == 0, "Entity properties changed (E-6):\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void The_browser_app_names_entities_only_by_golden_stored_names()
    {
        // RecordLockGuard EntityType="TRN.Trip", LiveRefresh filters and column views send these strings (E-9, E-15).
        var known = GoldenByName.Keys.Concat(Golden.Strings(Golden.Security["viewKeys"])).ToHashSet(StringComparer.Ordinal);
        var client = Path.Combine(Golden.RepoRoot, "src", "DirectService.Transportation.Web.Client");
        var used = Directory.EnumerateFiles(client, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal)) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\"((?:TRN|ACC|CFG|CORE)\\.[A-Za-z]+)\"").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(used);
        var strange = used.Where(v => !known.Contains(v)).ToList();
        Assert.True(strange.Count == 0, "The browser app names records by values that are not golden stored names: " + string.Join(", ", strange));
        // Everything the browser app sent at the restore tag is still a known name.
        var gone = Golden.Strings(Golden.Keys["clientStoredNameLiterals"]).Where(v => !known.Contains(v)).ToList();
        Assert.True(gone.Count == 0, "Names the browser app sent at the restore tag are no longer known: " + string.Join(", ", gone));
    }
}
