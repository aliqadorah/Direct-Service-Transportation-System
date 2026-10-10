using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// The full copy of a company's data (5.6, P-16, DX-04) for its Owner, also during the retention period: one ZIP with every company table as
/// JSON (machine-readable package, <c>json/&lt;Table&gt;.json</c>) and as an Excel sheet (<c>excel/&lt;Table&gt;.xlsx</c> — master data, trips,
/// documents and the full ledger). Internal keys never leave the platform (SEC-22): each row's id is its public id and every reference is
/// the public id of the referenced row; tenant/company keys, row versions, encrypted credentials and binary content are left out.
/// </summary>
public sealed class CompanyExportService(IAppDbContext db, IRequestContext ctx, ISpreadsheetService sheets, TimeProvider clock)
{
    private static readonly HashSet<string> Skipped = ["TenantId", "CompanyId", "RowVersion", "ClientIdEncrypted", "SecretKeyEncrypted", "LogoFile", "PasswordHash",
        "SecurityStamp", "TwoFactorSecret", "TokenHash", "CodeHash"];

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly MethodInfo RowsMethod = typeof(CompanyExportService).GetMethod(nameof(RowsAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo MapMethod = typeof(CompanyExportService).GetMethod(nameof(PublicIdsAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly Dictionary<Type, Dictionary<long, Guid>> _maps = [];

    public async Task<GeneratedFile> ExportAsync(CancellationToken ct)
    {
        var companyId = ctx.CompanyId ?? throw new InvalidOperationException("A company export needs a company.");
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var types = db.Model.GetEntityTypes().Where(e => typeof(ICompanyScoped).IsAssignableFrom(e.ClrType) && typeof(AuditableEntity).IsAssignableFrom(e.ClrType))
                .OrderBy(Name, StringComparer.Ordinal).ToList();
            foreach (var entity in types)
            {
                var columns = ((IReadOnlyEntityType)entity).GetProperties().Cast<IProperty>().Where(p => !Skipped.Contains(p.Name) && p.PropertyInfo is not null).ToList();
                var rows = await ((Task<List<Dictionary<string, object?>>>)RowsMethod.MakeGenericMethod(entity.ClrType).Invoke(this, [entity, columns, ct])!).ConfigureAwait(false);
                var name = Name(entity);
                await using (var json = zip.CreateEntry("json/" + name + ".json", CompressionLevel.Optimal).Open())
                    await JsonSerializer.SerializeAsync(json, rows, Json, ct).ConfigureAwait(false);
                if (rows.Count == 0) continue;
                var headers = rows[0].Keys.ToList();
                var data = sheets.WriteXlsx(name.Length <= 31 ? name : name[..31], headers, rows.Select(r => (IReadOnlyList<object?>)headers.Select(h => Cell(r[h])).ToList()));
                await using var excel = zip.CreateEntry("excel/" + name + ".xlsx", CompressionLevel.Optimal).Open();
                await excel.WriteAsync(data, ct).ConfigureAwait(false);
            }
            var company = await db.Set<Company>().AsNoTracking().Where(c => c.Id == companyId)
                .Select(c => new { c.PublicId, c.Name, c.NameEn, c.TaxNumber, c.CommercialRegNo, c.Address, c.Phone, c.Email, c.CountryCode, c.CurrencyCode, c.CurrencyDecimals, c.TaxRate,
                    c.TaxInclusive, c.TimeZone, c.Status }).SingleAsync(ct).ConfigureAwait(false);
            await using var manifest = zip.CreateEntry("company.json").Open();
            await JsonSerializer.SerializeAsync(manifest, new { company, exportedAt = clock.GetUtcNow().UtcDateTime, tables = types.Count }, Json, ct).ConfigureAwait(false);
        }
        return new GeneratedFile("company-export-" + clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + ".zip",
            "application/zip", buffer.ToArray());
    }

    /// <summary>Schema.Table of an entity (the relational table name annotation), or its type name.</summary>
    private static string Name(IEntityType e) =>
        (e.FindAnnotation("Relational:Schema")?.Value as string is { } schema ? schema + "." : string.Empty) + (e.FindAnnotation("Relational:TableName")?.Value as string ?? e.ClrType.Name);

    private static object? Cell(object? v) => v switch
    {
        Guid g => g.ToString(),
        _ => v,
    };

    /// <summary>
    /// The rows of one table: id → public id, references → public ids of the referenced rows, other columns as stored. No internal key ever
    /// leaves (SEC-22, review R2): an id without a foreign key is resolved through its type column (<c>SourceType</c>+<c>SourceId</c>,
    /// <c>PartyType</c>+<c>PartyId</c>, <c>EntityType</c>+<c>EntityId</c>) when that names a record type, and is left empty otherwise.
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> RowsAsync<T>(IEntityType entity, List<IProperty> columns, CancellationToken ct) where T : AuditableEntity
    {
        var foreignKeys = entity.GetForeignKeys().Where(fk => fk.Properties.Count == 1).ToDictionary(fk => fk.Properties[0].Name, fk => fk.PrincipalEntityType.ClrType);
        var byName = columns.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var rows = await db.Set<T>().AsNoTracking().OrderBy(e => e.Id).ToListAsync(ct).ConfigureAwait(false);
        var result = new List<Dictionary<string, object?>>(rows.Count);
        foreach (var row in rows)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = row.PublicId };
            foreach (var p in columns)
            {
                if (p.Name is "Id" or "PublicId" || p.PropertyInfo is null) continue;
                var value = p.PropertyInfo.GetValue(row);
                // Counters and other numbers stay; a key column (a foreign key, or a name ending in Id/By) never leaves as an internal id.
                if (value is long key && (foreignKeys.ContainsKey(p.Name) || p.Name.EndsWith("Id", StringComparison.Ordinal) || p.Name.EndsWith("By", StringComparison.Ordinal)))
                {
                    var target = foreignKeys.TryGetValue(p.Name, out var principal) ? principal
                        : p.Name.EndsWith("Id", StringComparison.Ordinal) && byName.TryGetValue(p.Name[..^2] + "Type", out var typeColumn) ? RecordType(typeColumn.PropertyInfo?.GetValue(row) as string)
                        : null;
                    value = target is not null && typeof(AuditableEntity).IsAssignableFrom(target)
                        && (await MapAsync(target, ct).ConfigureAwait(false)).TryGetValue(key, out var pid) ? pid : null;
                }
                d[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = value;
            }
            result.Add(d);
        }
        return result;
    }

    private Dictionary<string, Type>? _recordTypes;

    /// <summary>The record type named by a type column ("TRN.Trip", "Trip", "Customer"), or null.</summary>
    private Type? RecordType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        _recordTypes ??= db.Model.GetEntityTypes().Select(e => e.ClrType).Where(t => typeof(AuditableEntity).IsAssignableFrom(t))
            .GroupBy(t => t.Name, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _recordTypes.GetValueOrDefault(name[(name.LastIndexOf('.') + 1)..]);
    }

    private async Task<Dictionary<long, Guid>> MapAsync(Type type, CancellationToken ct)
    {
        if (_maps.TryGetValue(type, out var map)) return map;
        map = await ((Task<Dictionary<long, Guid>>)MapMethod.MakeGenericMethod(type).Invoke(this, [ct])!).ConfigureAwait(false);
        _maps[type] = map;
        return map;
    }

    /// <summary>Id → public id of a referenced table (deleted rows included; tenant and company filters still apply).</summary>
    private Task<Dictionary<long, Guid>> PublicIdsAsync<T>(CancellationToken ct) where T : AuditableEntity =>
        db.Set<T>().AsNoTracking().IgnoreQueryFilters(["SoftDelete"]).ToDictionaryAsync(e => e.Id, e => e.PublicId, ct);
}
