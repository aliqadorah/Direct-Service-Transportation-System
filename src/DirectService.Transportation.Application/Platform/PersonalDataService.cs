using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// Erasure of personal data on request (SEC-19, Jordan PDPL): records are never deleted, so the personal fields of an archived driver or user
/// are cleared in place — name, national ID, phones, address, bank details for drivers; name, e-mail, password and second factor for users —
/// with a reason in the audit log; earlier values in the append-only audit log are masked in every view of it (<see cref="ErasedRecords"/>). Trips and documents keep their numbers and amounts. The legal confirmation is the owner's (19.3).
/// </summary>
public sealed class PersonalDataService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IUrlTokenService tokens, IChangeContext changes, GrantGuard guard)
{
    public async Task AnonymiseDriverAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var d = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        if (d.Status != MasterCodes.Archived) throw Fail.Rule(ErrorCodes.ValidationFailed);
        d.FullName = "Anonymised driver " + d.PublicId.ToString("N")[..6];
        d.NationalId = "X" + d.PublicId.ToString("N")[..9];   // the column holds 10 characters
        d.LicenceNo = d.NationalId;
        d.Phone1 = "0000000000";
        d.Phone2 = null;
        d.Address = null;
        d.Iban = null;
        d.CliqAlias = null;
        d.BankName = null;
        d.PaymentMethod = "Cash";
        d.Notes = null;
        d.CustomValues = null;
        changes.SetAction(d, ErasedRecords.EraseAction, why);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

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
        if (u.Status != "Archived" || u.Id == ctx.UserId) throw Fail.Rule(ErrorCodes.ValidationFailed);
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
