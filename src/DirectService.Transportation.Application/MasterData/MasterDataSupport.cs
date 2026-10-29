using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Audit;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.MasterData;

/// <summary>Stored values and names of the master-data module.</summary>
public static class MasterCodes
{
    public const string Active = "Active";
    public const string Archived = "Archived";
    public const string Own = "Own";
    public const string External = "External";

    public static readonly IReadOnlyList<string> PaymentMethods = ["Cash", "Bank", "CliQ"];
    public static readonly IReadOnlyList<string> FleetClasses = [Own, External];

    /// <summary>Documents expiring within this many days raise alerts (FR-154).</summary>
    public const int AlertDays = 30;
}

/// <summary>
/// Shared work of the master-data services (M04): finding records by token, list status/sort/paging, reference counts with
/// their labels (BR-040, FR-162), history from the audit log (FR-166), market formats and the company's local date.
/// </summary>
public sealed class MasterDataSupport(IAppDbContext db, IUrlTokenService tokens, IRequestContext ctx, TimeProvider clock, IAuditDescriber describer)
{
    private string? _phonePattern;
    private string? _ibanPattern;
    private DateOnly? _today;

    public bool English => ctx.Language == "en";

    // ------------------------------------------------------------ finding

    public async Task<T> FindAsync<T>(string? token, CancellationToken ct) where T : AuditableEntity
    {
        var publicId = tokens.Decode<T>(token) ?? throw Fail.NotFound();
        return await db.Set<T>().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }

    /// <summary>The record named by a client id of a repeated create (API-05), or null.</summary>
    public async Task<T?> ExistingAsync<T>(Guid? clientId, CancellationToken ct) where T : AuditableEntity =>
        clientId is Guid g ? await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == g, ct).ConfigureAwait(false) : null;

    /// <summary>
    /// An active record chosen on a form (e.g. a driver's ownership type or partner). Unknown → 400 on the field; archived →
    /// 422 "archived and cannot be chosen on new entries" unless it is the value already stored.
    /// </summary>
    public async Task<T> ChooseAsync<T>(string field, string? token, long? currentId, Func<T, string> name, CancellationToken ct) where T : AuditableEntity
    {
        var publicId = tokens.Decode<T>(token) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, new FieldRef(field));
        var record = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false)
            ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, new FieldRef(field));
        ArgumentNullException.ThrowIfNull(name);
        if (record is IArchivable { Status: MasterCodes.Archived } && record.Id != currentId) throw Fail.RuleOn(field, ErrorCodes.RecordArchived, name(record));
        return record;
    }

    public string Token<T>(Guid publicId) => tokens.Encode<T>(publicId);

    /// <summary>The PublicId of a token of type <typeparamref name="T"/>, or null when malformed or of another type.</summary>
    public Guid? Decode<T>(string? token) => tokens.Decode<T>(token);

    /// <summary>
    /// Saves a new record. When the create carried a client id and the save fails (two identical requests racing, review M4), the
    /// record created by the other request is returned if it is in this company; a client id used elsewhere is refused (400).
    /// </summary>
    public async Task<T?> SaveCreateAsync<T>(Guid? clientId, CancellationToken ct) where T : AuditableEntity
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateException) when (clientId is not null)
        {
            db.ChangeTracker.Clear();
            if (await ExistingAsync<T>(clientId, ct).ConfigureAwait(false) is { } existing) return existing;
            var g = clientId.Value;
            if (await db.Set<T>().IgnoreQueryFilters().AnyAsync(x => x.PublicId == g, ct).ConfigureAwait(false))
                throw Fail.Field("clientId", ErrorCodes.ValidationFormat, new FieldRef("clientId"));
            throw;
        }
    }

    // ------------------------------------------------------------ lists (FR-165)

    /// <summary>Status filter: active (default), archived or all.</summary>
    public static IQueryable<T> WithStatus<T>(IQueryable<T> q, string? status) where T : IArchivable => (status?.Trim().ToLowerInvariant()) switch
    {
        null or "" or "active" => q.Where(x => x.Status == MasterCodes.Active),
        "archived" => q.Where(x => x.Status == MasterCodes.Archived),
        "all" => q,
        _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, new FieldRef("status")),
    };

    /// <summary>Sort by a white-listed field ("-" prefix for descending); anything else → 400 (API-04).</summary>
    public static IOrderedQueryable<T> Sort<T>(IQueryable<T> q, string? sort, IReadOnlyDictionary<string, Expression<Func<T, object?>>> fields, string defaultField)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var text = string.IsNullOrWhiteSpace(sort) ? defaultField : sort.Trim();
        var descending = text.StartsWith('-');
        var key = descending ? text[1..] : text;
        if (!fields.TryGetValue(key, out var selector)) throw Fail.Field("sort", ErrorCodes.ValidationFormat, new FieldRef("sort"));
        return descending ? q.OrderByDescending(selector) : q.OrderBy(selector);
    }

    public static async Task<PagedResult<TOut>> PageAsync<T, TOut>(IOrderedQueryable<T> q, int page, int pageSize, Func<T, TOut> map, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(map);
        var paging = new PageRequest(page, pageSize);
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var items = await q.Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<TOut>(items.Select(map).ToList(), paging.SafePage, paging.SafePageSize, total);
    }

    /// <summary>Search text for LIKE filters: trimmed, ASCII digits, at most 100 characters; null when empty.</summary>
    public static string? SearchText(string? q)
    {
        var text = TextNormalizer.CleanText(TextNormalizer.ToAsciiDigits(q));
        return text is null ? null : text.Length <= 100 ? text : text[..100];
    }

    // ------------------------------------------------------------ references (BR-040, FR-162)

    /// <summary>Reference counts with labels in the user's language (only types with a count).</summary>
    public IReadOnlyList<ReferenceCount> References(params (string Type, int Count)[] uses) =>
        uses.Where(u => u.Count > 0).Select(u => new ReferenceCount(u.Type, UseName(u.Type), u.Count)).ToList();

    /// <summary>Refuses a delete while referenced: "Cannot delete — linked to 4 trips · 1 tariff line. Archive instead." (US-25).</summary>
    public static void EnsureUnreferenced(IReadOnlyList<ReferenceCount> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count == 0) return;
        throw Fail.Rule(ErrorCodes.RecordInUse, string.Join(" · ", references.Select(r => $"{r.Count} {r.Label}")));
    }

    private string UseName(string type) => (English, type) switch
    {
        (true, "Trips") => "trips",
        (false, "Trips") => "رحلات",
        (true, "Invoices") => "invoices",
        (false, "Invoices") => "فواتير",
        (true, "Receipts") => "receipts",
        (false, "Receipts") => "سندات قبض",
        (true, "CreditNotes") => "credit notes",
        (false, "CreditNotes") => "إشعارات دائنة",
        (true, "Tariffs") => "tariff lines",
        (false, "Tariffs") => "بنود تعرفة",
        (true, "Advances") => "driver advances",
        (false, "Advances") => "سلف سائقين",
        (true, "Settlements") => "driver settlements",
        (false, "Settlements") => "تسويات سائقين",
        (true, "Drivers") => "drivers",
        (false, "Drivers") => "سائقين",
        (true, "PartnerPayments") => "partner payments",
        (false, "PartnerPayments") => "دفعات شركاء",
        (true, "MonthlySiteRates") => "monthly site rates",
        (false, "MonthlySiteRates") => "أسعار شهرية للمواقع",
        (true, "ApArDocuments") => "payable/receivable documents",
        (false, "ApArDocuments") => "مستندات ذمم",
        (true, "Customers") => "transport customers",
        (false, "Customers") => "عملاء نقل",
        (true, "EntryLines") => "journal lines",
        (false, "EntryLines") => "سطور قيود",
        _ => type,
    };

    // ------------------------------------------------------------ history (FR-166, FR-153)

    /// <summary>
    /// History of a record from core.AuditLogs, newest first (at most 200 entries), readable (owner feedback 14 Oct 2026, DT-188): each event
    /// as a sentence in the reader's language with its labelled changes, a journal entry's lines and links to the documents it names
    /// (<see cref="IAuditDescriber"/>). Technical-only edits (e.g. the journal link set by a posting) are left out.
    /// </summary>
    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string entityType, long entityId, CancellationToken ct)
    {
        var companyId = ctx.CompanyId;
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(200)
            .Select(a => new AuditRow(a.Id, a.OccurredAt, a.UserId, a.Action, a.EntityType, a.EntityId, a.OldValues, a.NewValues))
            .ToListAsync(ct).ConfigureAwait(false);
        var described = await describer.DescribeAsync(rows, new DescribeOptions(), ct).ConfigureAwait(false);
        return described.Where(h => !h.IsTechnicalOnly)
            .Select(h => new HistoryEntry(DateTime.SpecifyKind(h.Row.OccurredAt, DateTimeKind.Utc), h.User, h.Row.Action, h.Reason, h.Changes, h.Sentence, h.ActionLabel,
                h.Lines.Count == 0 ? null : h.Lines, h.Links.Count == 0 ? null : h.Links))
            .ToList();
    }

    // ------------------------------------------------------------ market formats and dates

    public async Task<bool> IsPhoneAsync(string phone, CancellationToken ct)
    {
        _phonePattern ??= await SettingAsync(SettingKeys.PhonePattern, ct).ConfigureAwait(false) ?? SettingKeys.JordanPhonePattern;
        return Matches(phone, _phonePattern);
    }

    public async Task<bool> IsIbanAsync(string iban, CancellationToken ct)
    {
        _ibanPattern ??= await SettingAsync(SettingKeys.IbanPattern, ct).ConfigureAwait(false) ?? SettingKeys.JordanIbanPattern;
        return Matches(iban, _ibanPattern);
    }

    /// <summary>A market pattern match; a pattern that times out counts as "does not match" instead of failing the request (review L3).</summary>
    private static bool Matches(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private Task<string?> SettingAsync(string key, CancellationToken ct) =>
        db.Set<CompanySetting>().AsNoTracking().Where(s => s.SettingKey == key).Select(s => s.SettingValue).SingleOrDefaultAsync(ct);

    /// <summary>Today in the company's time zone (expiry alerts, FR-154).</summary>
    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        if (_today is DateOnly d) return d;
        var zone = await db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => c.TimeZone).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (zone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var tz)) now = TimeZoneInfo.ConvertTime(now, tz);
        _today = DateOnly.FromDateTime(now.DateTime);
        return _today.Value;
    }

    // ------------------------------------------------------------ text

    /// <summary>Person-typed name: whitespace collapsed, ASCII digits (DT-37); null when empty.</summary>
    public static string? Name(string? value) => TextNormalizer.CleanText(TextNormalizer.ToAsciiDigits(value));

    /// <summary>Phone, IBAN and similar codes: ASCII digits, no spaces or dashes; null when empty.</summary>
    public static string? Compact(string? value) => TextNormalizer.Compact(value);

    /// <summary>Truck plate: ASCII digits, upper case, no spaces (unique within the company, DT-37).</summary>
    public static string? Plate(string? value)
    {
        var v = TextNormalizer.CleanCode(value)?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return string.IsNullOrEmpty(v) ? null : v;
    }
}
