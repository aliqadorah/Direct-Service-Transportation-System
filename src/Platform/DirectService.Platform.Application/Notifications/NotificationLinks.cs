using DirectService.Platform.Application.Identity;
using DirectService.Platform.Contracts.Collaboration;
using DirectService.Platform.Contracts.Modules;
using DirectService.Platform.Contracts.Security;

namespace DirectService.Platform.Application.Notifications;

/// <summary>
/// Resolves the stored link of a notification when it is shown (DT-189), for every notification type: the record the link names is looked
/// up again, its current URL token is used, and the reader's permissions are checked — so links stored by older versions work too, and a
/// record that was deleted, or a screen the reader may not open, gives a clear reason instead of a broken page. The company part of the
/// address is the company's URL token, added by the caller (never its name). The platform's links (users, tenant screens, exports) here,
/// the links to a module's records and screens through its <see cref="INotificationTypeProvider"/> (Stage 2 session 14).
/// </summary>
public sealed class NotificationLinks(IRequestContext ctx, IFileStore files, AccessService access, IEnumerable<INotificationTypeProvider> providers)
{
    public const string Missing = NotificationTarget.Missing;
    public const string Forbidden = NotificationTarget.Forbidden;

    /// <summary>Targets of the notifications (type, stored link) of the current company, in order.</summary>
    public async Task<IReadOnlyList<NotificationTarget>> ResolveAsync(IReadOnlyList<(string Type, string? Link)> notes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var result = new NotificationTarget[notes.Count];
        bool? tenantManager = null;
        for (var i = 0; i < notes.Count; i++)
        {
            var (type, link) = notes[i];
            result[i] = link switch
            {
                null when type == NotificationService.InvitationAccepted => Screen("settings/users", PlatformPermissionKeys.UsersManage),
                null => NotificationTarget.None,
                _ when !link.StartsWith('/') || link.StartsWith("//", StringComparison.Ordinal) => NotificationTarget.None,
                _ when link.StartsWith("/tenant/", StringComparison.Ordinal) =>
                    (tenantManager ??= ctx.UserId is long me && await access.HasTenantPermissionAsync(me, PlatformPermissionKeys.TenantManage, ct).ConfigureAwait(false))
                        ? new(link, null) : new(null, Forbidden),
                _ when link.StartsWith("/exports/", StringComparison.Ordinal) => await ExportAsync(link["/exports/".Length..], ct).ConfigureAwait(false),
                _ => await ModuleLinkAsync(link, ct).ConfigureAwait(false) ?? new(link.TrimStart('/'), null),
            };
        }
        return result;
    }

    private async Task<NotificationTarget?> ModuleLinkAsync(string link, CancellationToken ct)
    {
        foreach (var provider in providers)
            if (await provider.ResolveLinkAsync(link, ct).ConfigureAwait(false) is { } target) return target;
        return null;
    }

    private NotificationTarget Screen(string route, params string[] permissions) =>
        permissions.Any(ctx.HasPermission) ? new(route, null) : new(null, Forbidden);

    /// <summary>An export file is kept seven days, for its requester in that company only.</summary>
    private async Task<NotificationTarget> ExportAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out var g) || ctx.TenantId is not long tenant || ctx.CompanyId is not long company || ctx.UserPublicId is not Guid user)
            return new(null, Missing);
        var found = await files.ListAsync(ExportFiles.Folder(tenant, company, user) + g.ToString("N") + "/", ct).ConfigureAwait(false);
        return found.Count > 0 ? new("exports/" + g.ToString("N"), null) : new(null, Missing);
    }
}
