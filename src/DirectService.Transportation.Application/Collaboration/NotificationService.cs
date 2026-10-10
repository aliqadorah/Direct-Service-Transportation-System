using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// In-app notifications (FR-190): rejected e-invoices, finished exports, failed postings, accepted invitations, support-access requests.
/// A notification belongs to one user (and a company when it concerns one); its text is stored in both languages (JSON {"ar","en"},
/// DT-120) and shown in the reader's language; new ones are pushed to the user's connections (RT-01). Users list their own notifications
/// and mark them read; nobody else can.
/// </summary>
public sealed class NotificationService(IAppDbContext db, IRequestContext ctx, IUrlTokenService tokens, IServiceProvider services, TimeProvider clock, IAfterCommit afterCommit)
{
    public const string EInvoiceRejected = "EINV.Rejected";
    public const string PostingFailed = "ACC.PostingFailed";
    public const string ExportReady = "CORE.ExportReady";
    public const string ExportFailed = "CORE.ExportFailed";

    private static readonly string[] TenantFilter = ["Tenant"];

    private sealed record Text(string Ar, string En);

    /// <summary>Active users holding <paramref name="permission"/> in the company (recipients of a company event).</summary>
    public async Task<IReadOnlyList<long>> HoldersAsync(long companyId, string permission, CancellationToken ct) =>
        await db.Set<UserCompanyRole>().AsNoTracking().IgnoreQueryFilters(TenantFilter)
            .Where(r => r.CompanyId == companyId && r.User.Status == "Active" && !r.Role.IsDeleted && db.Set<RolePermission>().IgnoreQueryFilters(TenantFilter)
                .Any(p => p.RoleId == r.RoleId && p.Permission.PermissionKey == permission))
            .Select(r => r.UserId).Distinct().ToListAsync(ct).ConfigureAwait(false);

    /// <summary>Adds a notification for each user and saves (inside the caller's transaction when there is one), then pushes it.</summary>
    public async Task NotifyAsync(IEnumerable<long> userIds, long? companyId, string type, string ar, string en, string? link, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var tenantId = ctx.TenantId ?? await db.Set<Company>().AsNoTracking().IgnoreQueryFilters().Where(c => c.Id == companyId).Select(c => (long?)c.TenantId)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("A notification needs a tenant.");
        var message = Message(ar, en);
        var rows = ids.Select(u => new Notification { TenantId = tenantId, UserId = u, CompanyId = companyId, NotificationType = type, Message = message, Link = link }).ToList();
        db.Set<Notification>().AddRange(rows);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var notifier = services.GetService(typeof(IRealtimeNotifier)) as IRealtimeNotifier;
        if (notifier is null) return;
        // Pushed once the caller's transaction commits, so nobody is told about a notification that was rolled back (review M3).
        var users = await db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.PublicId }).ToDictionaryAsync(u => u.Id, u => u.PublicId, ct)
            .ConfigureAwait(false);
        var pushes = rows.Select(n => (User: users[n.UserId], Payload: (object)new { id = tokens.Encode<Notification>(n.PublicId), type, link })).ToList();
        afterCommit.Add(async () =>
        {
            foreach (var (user, payload) in pushes)
            {
                try
                {
                    await notifier.NotifyUserAsync(user, payload, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Pushing is best effort; the notification is stored.
                }
            }
        });
    }

    /// <summary>Unescaped JSON (Arabic stays readable and short) trimmed so it fits the 500-character column (review H1).</summary>
    private static readonly JsonSerializerOptions Store = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public const int MaxMessage = 500;

    public static string Message(string ar, string en)
    {
        // Share the space: each language gets half, or more when the other needs less; then trim until the JSON fits.
        var budget = MaxMessage - JsonSerializer.Serialize(new Text(string.Empty, string.Empty), Store).Length - 2;
        var half = budget / 2;
        var a = ar.Length + en.Length <= budget ? ar : Cut(ar, Math.Max(half, budget - Math.Min(en.Length, half)));
        var e = ar.Length + en.Length <= budget ? en : Cut(en, budget - a.Length);
        var json = JsonSerializer.Serialize(new Text(a, e), Store);
        while (json.Length > MaxMessage)
        {
            if (a.Length >= e.Length) a = Cut(a, a.Length - 2);
            else e = Cut(e, e.Length - 2);
            json = JsonSerializer.Serialize(new Text(a, e), Store);
        }
        return json;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : max <= 1 ? "…" : text[..(max - 1)] + "…";

    /// <summary>The caller's notifications in the current company (and those without a company), newest first; unread count.</summary>
    public async Task<NotificationPage> ListAsync(bool unreadOnly, CancellationToken ct)
    {
        var me = ctx.UserId ?? throw Fail.Forbidden();
        var companyId = ctx.CompanyId;
        var query = db.Set<Notification>().AsNoTracking().Where(n => n.UserId == me && (n.CompanyId == null || n.CompanyId == companyId));
        var unread = await query.CountAsync(n => n.ReadAt == null, ct).ConfigureAwait(false);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);
        var rows = await query.OrderByDescending(n => n.Id).Take(100)
            .Select(n => new { n.PublicId, n.NotificationType, n.Message, n.Link, Company = n.Company != null ? n.Company.Name : null, n.CreatedAt, n.ReadAt })
            .ToListAsync(ct).ConfigureAwait(false);
        return new NotificationPage(rows.Select(n => new NotificationResponse(tokens.Encode<Notification>(n.PublicId), n.NotificationType, Render(n.Message), n.Link, n.Company,
            DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc), n.ReadAt is DateTime r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null)).ToList(), unread);
    }

    /// <summary>The message in the reader's language; older plain-text messages as stored.</summary>
    private string Render(string message)
    {
        if (!message.StartsWith('{')) return message;
        try
        {
            var t = JsonSerializer.Deserialize<Text>(message);
            return t is null ? message : ctx.Language == "en" ? t.En : t.Ar;
        }
        catch (JsonException)
        {
            return message;
        }
    }

    public async Task MarkReadAsync(string token, CancellationToken ct)
    {
        var me = ctx.UserId ?? throw Fail.Forbidden();
        var pid = tokens.Decode<Notification>(token) ?? throw Fail.NotFound();
        var n = await db.Set<Notification>().SingleOrDefaultAsync(x => x.PublicId == pid && x.UserId == me, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        n.ReadAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var me = ctx.UserId ?? throw Fail.Forbidden();
        var companyId = ctx.CompanyId;
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Set<Notification>().Where(n => n.UserId == me && n.ReadAt == null && (n.CompanyId == null || n.CompanyId == companyId))
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAt, now), ct).ConfigureAwait(false);
    }
}
