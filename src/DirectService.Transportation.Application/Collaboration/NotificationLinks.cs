using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Acc;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using DirectService.Platform.Application.Identity;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>Where a notification leads: a screen route (company-relative, or absolute for tenant screens) or why it cannot be opened.</summary>
public sealed record NotificationTarget(string? Route, string? Problem)
{
    public static readonly NotificationTarget None = new(null, null);
}

/// <summary>
/// Resolves the stored link of a notification when it is shown (DT-189), for every notification type: the record the link names is looked
/// up again, its current URL token is used, and the reader's permissions are checked — so links stored by older versions work too, and a
/// record that was deleted, or a screen the reader may not open, gives a clear reason instead of a broken page. The company part of the
/// address is the company's URL token, added by the caller (never its name).
/// </summary>
public sealed class NotificationLinks(IAppDbContext db, IRequestContext ctx, IUrlTokenService tokens, IFileStore files, AccessService access)
{
    public const string Missing = "missing";
    public const string Forbidden = "forbidden";

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
                null when type == NotificationService.InvitationAccepted => Screen("settings/users", PermissionKeys.UsersManage),
                null => NotificationTarget.None,
                _ when !link.StartsWith('/') || link.StartsWith("//", StringComparison.Ordinal) => NotificationTarget.None,
                _ when link.StartsWith("/tenant/", StringComparison.Ordinal) =>
                    (tenantManager ??= ctx.UserId is long me && await access.HasTenantPermissionAsync(me, PermissionKeys.TenantManage, ct).ConfigureAwait(false))
                        ? new(link, null) : new(null, Forbidden),
                _ when link.StartsWith(InvoiceSubmission, StringComparison.Ordinal) => await SubmissionAsync(link[InvoiceSubmission.Length..], ct).ConfigureAwait(false),
                _ when link.StartsWith(ApArSubmission, StringComparison.Ordinal) => await ApArSubmissionAsync(link[ApArSubmission.Length..], ct).ConfigureAwait(false),
                _ when link.StartsWith("/exports/", StringComparison.Ordinal) => await ExportAsync(link["/exports/".Length..], ct).ConfigureAwait(false),
                "/ledger/pending-postings" => Screen("accounting/automatic?tab=pending", PermissionKeys.JeExport),
                "/settings/e-invoicing" => Screen("settings/e-invoicing", PermissionKeys.SettingsManage),
                _ => new(link.TrimStart('/'), null),
            };
        }
        return result;
    }

    public const string InvoiceSubmission = "/einvoice-submissions/";
    public const string ApArSubmission = "/apar/e-invoice-submissions/";

    private NotificationTarget Screen(string route, params string[] permissions) =>
        permissions.Any(ctx.HasPermission) ? new(route, null) : new(null, Forbidden);

    /// <summary>A rejected invoice or credit note opens the document itself (the e-invoicing status is shown there).</summary>
    private async Task<NotificationTarget> SubmissionAsync(string token, CancellationToken ct)
    {
        if (tokens.Decode<EInvoiceSubmission>(token) is not Guid pid) return new(null, Missing);
        var s = await db.Set<EInvoiceSubmission>().AsNoTracking().Where(x => x.PublicId == pid)
            .Select(x => new { Invoice = x.Invoice != null && !x.Invoice.IsDeleted ? (Guid?)x.Invoice.PublicId : null, Credit = x.CreditNote != null && !x.CreditNote.IsDeleted ? (Guid?)x.CreditNote.PublicId : null })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (s?.Invoice is Guid i) return Screen("invoices/" + tokens.Encode<Invoice>(i), PermissionKeys.InvView);
        if (s?.Credit is Guid c) return Screen("credit-notes/" + tokens.Encode<CreditNote>(c), PermissionKeys.InvView);
        return new(null, Missing);
    }

    private async Task<NotificationTarget> ApArSubmissionAsync(string token, CancellationToken ct)
    {
        if (tokens.Decode<ApArEInvoiceSubmission>(token) is not Guid pid) return new(null, Missing);
        var s = await db.Set<ApArEInvoiceSubmission>().AsNoTracking().Where(x => x.PublicId == pid)
            .Select(x => new { Doc = x.ApArDocument != null && !x.ApArDocument.IsDeleted ? (Guid?)x.ApArDocument.PublicId : null, Ret = x.ApArReturn != null && !x.ApArReturn.IsDeleted ? (Guid?)x.ApArReturn.PublicId : null })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (s?.Doc is Guid d) return Screen("accounting/receivables/" + tokens.Encode<ApArDocument>(d), PermissionKeys.AcctApar, PermissionKeys.AcctView);
        if (s?.Ret is Guid r) return Screen("accounting/apar-returns/" + tokens.Encode<ApArReturn>(r), PermissionKeys.AcctApar, PermissionKeys.AcctView);
        return new(null, Missing);
    }

    /// <summary>An export file is kept seven days, for its requester in that company only.</summary>
    private async Task<NotificationTarget> ExportAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out var g) || ctx.TenantId is not long tenant || ctx.CompanyId is not long company || ctx.UserPublicId is not Guid user)
            return new(null, Missing);
        var found = await files.ListAsync(ExportFiles.Folder(tenant, company, user) + g.ToString("N") + "/", ct).ConfigureAwait(false);
        return found.Count > 0 ? new("exports/" + g.ToString("N"), null) : new(null, Missing);
    }
}
