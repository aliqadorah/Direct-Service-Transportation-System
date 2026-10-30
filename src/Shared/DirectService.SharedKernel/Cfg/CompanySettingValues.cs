using Microsoft.EntityFrameworkCore;

namespace DirectService.SharedKernel;

/// <summary>
/// Reads and writes the current company's settings (cfg.CompanySettings, one row per key; every change audited old → new, FR-168) for the
/// platform's company settings and a module's own section (Stage 2 session 14; was inside <c>CompanySettingsService</c>).
/// </summary>
public static class CompanySettingValues
{
    /// <summary>Every setting of the current company by key.</summary>
    public static async Task<Dictionary<string, string>> AllAsync(IAppDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.Set<CompanySetting>().AsNoTracking().ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, ct).ConfigureAwait(false);
    }

    /// <summary>The value of one setting of the current company; null when it was never saved.</summary>
    public static async Task<string?> ValueAsync(IAppDbContext db, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.Set<CompanySetting>().AsNoTracking().Where(s => s.SettingKey == key).Select(s => s.SettingValue).SingleOrDefaultAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Adds or changes a setting (saved by the caller); a change is audited as "SettingChange".</summary>
    public static async Task SetAsync(IAppDbContext db, IChangeContext changes, string key, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(changes);
        var s = await db.Set<CompanySetting>().SingleOrDefaultAsync(x => x.SettingKey == key, ct).ConfigureAwait(false);
        if (s is null)
        {
            db.Set<CompanySetting>().Add(new CompanySetting { SettingKey = key, SettingValue = value });
        }
        else if (s.SettingValue != value)
        {
            s.SettingValue = value;
            changes.SetAction(s, "SettingChange");
        }
    }
}
