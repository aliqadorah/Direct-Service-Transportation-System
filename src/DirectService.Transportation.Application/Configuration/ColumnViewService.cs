using System.Text.Json;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Configuration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Cfg;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Configuration;

/// <summary>
/// Saved column layouts of list screens (FR-183, FR-165): the company default (UserId NULL, settings.manage) and each user's
/// personal view (any member). The effective view is the personal one, else the company default.
/// </summary>
public sealed partial class ColumnViewService(IAppDbContext db, IRequestContext ctx)
{
    public const int MaxColumns = 100;
    public const int MaxViewsPerUser = 200;
    private const string Personal = "Personal";
    private const string CompanyDefault = "CompanyDefault";
    private const string BuiltIn = "BuiltIn";

    public async Task<ColumnViewResponse> GetAsync(string viewKey, CancellationToken ct)
    {
        EnsureKey(viewKey);
        var userId = ctx.UserId;
        var views = await db.Set<TableColumnView>().AsNoTracking()
            .Where(v => v.ViewKey == viewKey && (v.UserId == null || v.UserId == userId)).ToListAsync(ct).ConfigureAwait(false);
        var personal = views.FirstOrDefault(v => v.UserId == userId);
        var company = views.FirstOrDefault(v => v.UserId == null);
        var companyColumns = company is null ? null : Read(company.Columns);
        if (personal is not null) return new ColumnViewResponse(viewKey, Personal, Read(personal.Columns), companyColumns);
        return new ColumnViewResponse(viewKey, company is null ? BuiltIn : CompanyDefault, companyColumns ?? [], companyColumns);
    }

    public Task<ColumnViewResponse> SavePersonalAsync(string viewKey, ColumnViewRequest r, CancellationToken ct) =>
        SaveAsync(viewKey, ctx.UserId ?? throw Fail.Forbidden(), r, ct);

    public Task<ColumnViewResponse> SaveCompanyDefaultAsync(string viewKey, ColumnViewRequest r, CancellationToken ct) =>
        SaveAsync(viewKey, null, r, ct);

    /// <summary>Removes the personal view so the company default applies again.</summary>
    public async Task<ColumnViewResponse> ResetPersonalAsync(string viewKey, CancellationToken ct)
    {
        EnsureKey(viewKey);
        var userId = ctx.UserId;
        var view = await db.Set<TableColumnView>().SingleOrDefaultAsync(v => v.ViewKey == viewKey && v.UserId == userId, ct).ConfigureAwait(false);
        if (view is not null)
        {
            view.DeleteReason = "Reset to the company default";
            db.Set<TableColumnView>().Remove(view);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return await GetAsync(viewKey, ct).ConfigureAwait(false);
    }

    private async Task<ColumnViewResponse> SaveAsync(string viewKey, long? userId, ColumnViewRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        EnsureKey(viewKey);
        var columns = r.Columns ?? [];
        new Validator()
            .Check(columns.Count is > 0 and <= MaxColumns, "columns", ErrorCodes.ValidationRange)
            .Check(columns.All(c => c.Key is not null && ColumnKeyPattern().IsMatch(c.Key)), "columns")
            .Check(columns.All(c => c.Width is null or (>= 40 and <= 1000)), "columns", ErrorCodes.ValidationRange)
            .Check(columns.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() == columns.Count, "columns")
            .ThrowIfInvalid();
        var json = JsonSerializer.Serialize(columns.Select(c => new ColumnSetting(c.Key, c.Width)).ToList(), Json);
        var view = await db.Set<TableColumnView>().SingleOrDefaultAsync(v => v.ViewKey == viewKey && v.UserId == userId, ct).ConfigureAwait(false);
        if (view is null)
        {
            if (userId is not null && await db.Set<TableColumnView>().CountAsync(v => v.UserId == userId, ct).ConfigureAwait(false) >= MaxViewsPerUser)
                throw Fail.Field("viewKey", ErrorCodes.ValidationRange, "viewKey");
            view = new TableColumnView { ViewKey = viewKey, UserId = userId, Columns = json };
            db.Set<TableColumnView>().Add(view);
        }
        else
        {
            view.Columns = json;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(viewKey, ct).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static List<ColumnSetting> Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ColumnSetting>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void EnsureKey(string viewKey)
    {
        if (viewKey is null || viewKey.Length > 80 || !ViewKeyPattern().IsMatch(viewKey)) throw Fail.NotFound();
    }

    /// <summary>Namespaced list key, e.g. TRN.Trips, TRN.Drivers, ACC.Journal.</summary>
    [GeneratedRegex("^[A-Z]{2,5}\\.[A-Za-z][A-Za-z0-9]{1,60}$")]
    private static partial Regex ViewKeyPattern();

    /// <summary>Column key: a standard field or list column, or "custom.&lt;fieldKey&gt;".</summary>
    [GeneratedRegex("^(custom\\.)?[A-Za-z][A-Za-z0-9_]{0,49}$")]
    private static partial Regex ColumnKeyPattern();
}
