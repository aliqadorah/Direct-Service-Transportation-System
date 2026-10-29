using System.Reflection;
using DirectService.Transportation.Application.Notifications;
using DirectService.Database;
using DirectService.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DirectService.Architecture.Tests;

/// <summary>
/// Fixed keys stored in the database (platform design §e, E-4, E-8 … E-20): email template names, notification types, permission keys and
/// their seed rows, preset roles, setting keys, document types, posting keys, source and audit codes, report-builder sources.
/// </summary>
public sealed class StoredKeyTests
{
    private static readonly System.Text.Json.Nodes.JsonNode Keys = Golden.Keys;

    [Fact]
    public void Email_template_names_stored_in_the_outbox_are_unchanged()
    {
        // Each template method is called and the name it stores is compared (VerifyAddedEmail stores "VerifyEmail").
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in typeof(EmailTemplates).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(DirectService.Transportation.Application.Abstractions.EmailMessage)))
            actual[m.Name] = ((DirectService.Transportation.Application.Abstractions.EmailMessage)m.Invoke(null, m.GetParameters().Select(Dummy).ToArray())!).Template;
        Golden.SameMap("Email templates (core.EmailOutbox.Template, E-4)", Golden.Map(Keys["emailTemplates"]), actual);
        Golden.SameSet("Email template names", Golden.Map(Keys["emailTemplates"]).Values, Golden.Constants(typeof(EmailTemplateNames)).Values);
        Golden.SameSet("Single-use link templates", Golden.Strings(Keys["emailSecretLinkTemplates"]), EmailTemplates.SecretLinkTemplates);
    }

    private static object? Dummy(ParameterInfo p) =>
        p.ParameterType == typeof(string) ? (p.Name == "language" ? "en" : "x@example.com")
        : p.ParameterType == typeof(int) ? 1
        : p.ParameterType == typeof(bool) ? false
        : p.ParameterType == typeof(DateTime) || p.ParameterType == typeof(DateTime?) ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        : p.ParameterType == typeof(DateOnly) ? new DateOnly(2026, 1, 1)
        : null;

    [Fact]
    public void Notification_types_are_unchanged()
    {
        Golden.SameSet("Notification types (core.Notifications.NotificationType, E-18)", Golden.Strings(Keys["notificationTypes"]),
            DirectService.Transportation.Application.Collaboration.NotificationTexts.Types);
        Golden.SameMap("Notification type constants", Golden.Map(Keys["notificationConstants"]), Golden.Constants(Golden.TypeNamed("NotificationService")));
    }

    [Fact]
    public void Permission_keys_preset_roles_and_their_seed_rows_are_unchanged()
    {
        Golden.SameMap("Permission keys (core.Permissions, E-20)", Golden.Map(Keys["permissionKeys"]), Golden.Constants(Golden.TypeNamed("PermissionKeys")));
        Golden.SameMap("Preset roles", Golden.Map(Keys["presetRoles"]), Golden.Constants(Golden.TypeNamed("PresetRoles")));
        var grants = (IReadOnlyDictionary<string, IReadOnlyList<string>>)Golden.TypeNamed("PresetRoles").GetField("Permissions")!.GetValue(null)!;
        foreach (var (role, keys) in Keys["presetRoleGrants"]!.AsObject())
            Golden.SameSet("Grants of preset role " + role, Golden.Strings(keys), grants.TryGetValue(role, out var now) ? now : []);
        Assert.Equal(Keys["presetRoleGrants"]!.AsObject().Count, grants.Count);

        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var model = db.GetService<IDesignTimeModel>().Model;
        Golden.SameSet("Permission seed rows (Id, PublicId, key, group)", Rows(Keys["permissionSeed"]), Seed(model, typeof(DirectService.Platform.Domain.Permission), "Id", "PublicId", "PermissionKey", "GroupName"));
        Golden.SameSet("Plan seed rows (Id, PublicId, code)", Rows(Keys["planSeed"]), Seed(model, typeof(DirectService.Platform.Domain.Plan), "Id", "PublicId", "Code"));
    }

    private static IEnumerable<string> Rows(System.Text.Json.Nodes.JsonNode? rows) =>
        rows!.AsArray().Select(r => string.Join(" | ", r!.AsObject().Select(c => $"{c.Key}={c.Value?.GetValue<string>()}")));

    private static IEnumerable<string> Seed(IModel model, Type entity, params string[] columns) =>
        model.FindEntityType(entity)!.GetSeedData().Select(r => string.Join(" | ", columns.Select(c => $"{c}={(r.TryGetValue(c, out var v) ? v is Guid g ? g.ToString() : v?.ToString() : null)}")));

    [Fact]
    public void Setting_keys_document_types_and_posting_keys_are_unchanged()
    {
        Golden.SameMap("Company setting keys (cfg.CompanySettings, E-14)", Golden.Map(Keys["settingKeys"]), Golden.Constants(Golden.TypeNamed("SettingKeys")));
        Golden.SameMap("Document types (cfg.NumberSequences, E-13)", Golden.Map(Keys["documentTypes"]), Golden.Constants(Golden.TypeNamed("DocumentTypes")));
        Golden.SameMap("Posting keys (acc.PostingKeyMappings, E-12)", Golden.Map(Keys["postingKeys"]), Golden.Constants(Golden.TypeNamed("PostingKeys")));
    }

    [Fact]
    public void Accounting_codes_and_field_catalog_entity_types_are_unchanged()
    {
        // Module, source and party codes with their database CHECK lists (E-19) and the custom/standard field entity types (E-11).
        var codes = Golden.TypeNamed("AccountingCodes");
        Golden.SameMap("Accounting codes (acc.* CHECK values, E-19)", Golden.Map(Keys["accountingCodes"]), Golden.Constants(codes));
        foreach (var (list, values) in Keys["accountingCodeLists"]!.AsObject())
            Assert.Equal(Golden.Strings(values), (IEnumerable<string>)codes.GetField(list)!.GetValue(null)!);
        Golden.SameMap("Field catalog entity types (cfg.CustomFieldDefinitions, E-11)", Golden.Map(Keys["fieldCatalogEntityTypes"]), Golden.Constants(Golden.TypeNamed("FieldCatalog")));
    }

    [Fact]
    public void Source_and_audit_codes_are_unchanged()
    {
        // Constants named after their column (audit EntityType written by audit.Record, posting SourceType …), wherever they are declared (E-8, E-19).
        foreach (var (name, values) in Keys["codes"]!.AsObject())
        {
            var actual = Golden.AppAssemblies.SelectMany(a => a.GetTypes())
                .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name == name).Select(f => (string)f.GetRawConstantValue()!);
            Golden.SameSet($"Constants named {name}", Golden.Strings(values), actual);
        }
    }

    [Fact]
    public void Report_builder_sources_and_their_field_keys_are_unchanged()
    {
        // cfg.SavedReports.Source and the field keys inside its Definition (E-16).
        var sources = DirectService.Transportation.Application.Reports.ReportSources.All;
        Golden.SameSet("Report builder sources", Keys["reportSources"]!.AsObject().Select(p => p.Key), sources.Select(s => s.Key));
        foreach (var (key, fields) in Keys["reportSources"]!.AsObject())
            Golden.Kept($"Field keys of report source {key}", Golden.Strings(fields), sources.Single(s => s.Key == key).Fields.Select(f => f.Key));
    }
}
