using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Platform.Application.Identity;

/// <summary>
/// Erasure of a user's personal data on request (SEC-19, Jordan PDPL): records are never deleted, so the personal fields of an archived user
/// are cleared in place — name, e-mail, password and second factor — with a reason in the audit log; earlier values in the append-only audit
/// log are masked in every view of it (<see cref="ErasedRecords"/>). The legal confirmation is the owner's (19.3). Platform since Stage 2
/// session 14 (a module erases its own records, e.g. Transportation's <c>DriverPersonalDataService</c>).
/// </summary>
public sealed class PersonalDataService(IAppDbContext db, IRequestContext ctx, IUrlTokenService tokens, IChangeContext changes, GrantGuard guard)
{
    /// <summary>
    /// A user of the current company only, and an Owner-level user only by an Owner (the same checks as every other user action, SR-01);
    /// the user's invitation e-mails and session addresses are cleared too.
    /// </summary>
    public async Task AnonymiseUserAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var pid = tokens.Decode<User>(token) ?? throw Fail.NotFound();
        var u = await db.Set<UserCompanyRole>().Where(r => r.CompanyId == ctx.CompanyId && r.User.PublicId == pid && r.User.TenantId == ctx.TenantId && !r.User.IsDeleted)
            .Select(r => r.User).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        await guard.EnsureCanManageUserAsync(u.Id, ct).ConfigureAwait(false);
        if (u.Status != "Archived" || u.Id == ctx.UserId) throw Fail.Rule(SharedErrorCodes.ValidationFailed);
        var oldEmail = u.Email;
        foreach (var invitation in await db.Set<Invitation>().IgnoreQueryFilters(["Company"]).Where(i => i.TenantId == ctx.TenantId && i.Email == oldEmail)
                     .ToListAsync(ct).ConfigureAwait(false))
        {
            invitation.Email = "anonymised-" + invitation.PublicId.ToString("N") + "@anonymised.invalid";
            changes.SetAction(invitation, ErasedRecords.EraseAction, why);
        }
        await db.Set<UserSession>().IgnoreQueryFilters().Where(s => s.UserId == u.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IpAddress, (string?)null).SetProperty(x => x.UserAgent, (string?)null), ct).ConfigureAwait(false);
        var email = "anonymised-" + u.PublicId.ToString("N") + "@anonymised.invalid";
        u.FullName = "Anonymised user";
        u.Email = email;
        u.NormalizedEmail = email.ToUpperInvariant();
        u.UserName = null;   // revision 6: a username can identify the person
        u.NormalizedUserName = null;
        u.PasswordHash = string.Empty;
        u.SecurityStamp = Guid.NewGuid().ToString("N");
        u.TwoFactorSecret = null;
        changes.SetAction(u, ErasedRecords.EraseAction, why);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
