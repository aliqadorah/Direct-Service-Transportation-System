using DirectService.Platform.Application.Identity;
using DirectService.Platform.Domain;
using Microsoft.AspNetCore.Identity;
using DirectService.Platform.Contracts.Errors;
using DirectService.Platform.Contracts.Security;
using DirectService.Platform.Contracts.Settings;
using DirectService.Platform.Application.Tenants;

namespace DirectService.Platform.Application.Staff;

/// <summary>
/// Platform staff accounts by the operations command (D-13, DT-131) — for recovery only since owner feedback 13 Oct 2026 (the first account
/// comes from the first-run setup, later ones from invitations, see <see cref="ProviderSetupService"/> and <see cref="StaffService"/>):
/// `DirectService.Transportation.Web create-staff &lt;email&gt; &lt;full name&gt;`. Never by sign-up. The account belongs to no tenant, has no password and gets a set-password link by e-mail; the first sign-in
/// then requires setting up two-step verification like every user (SEC-03). Running it again for an existing staff account only sends a
/// new link. Recorded in the audit log under the platform system account.
/// </summary>
public sealed class StaffAccountService(IDataScope scope, UserManager<User> users, AccountService accounts, TenantLifecycleService lifecycle,
    IChangeContext changes, TimeProvider clock)
{
    public async Task<string> CreateAsync(string? email, string? fullName, CancellationToken ct)
    {
        var mail = email?.Trim();
        var name = TextNormalizer.CleanText(fullName);
        new Validator().Require("email", mail).MaxLength("email", mail, 150).Require("fullName", name).MaxLength("fullName", name, 150).ThrowIfInvalid();

        var system = await lifecycle.SystemUserAsync(ct).ConfigureAwait(false);
        using var _ = scope.Use(null, null, system);
        var existing = await users.FindByEmailAsync(mail!).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!existing.IsPlatformStaff || existing.TenantId is not null) throw Fail.Conflict(PlatformErrorCodes.EmailInUse);
            await accounts.SendPasswordResetAsync(existing, ct).ConfigureAwait(false);
            return "Existing staff account: a new set-password link was sent.";
        }

        var user = new User
        {
            FullName = name!, Email = mail!, LanguageCode = "ar", IsPlatformStaff = true, TenantId = null, EmailConfirmedAt = clock.GetUtcNow().UtcDateTime,
        };
        changes.SetAction(user, "CreateStaff");
        var created = await users.CreateAsync(user).ConfigureAwait(false);   // no password: set through the e-mailed link
        if (!created.Succeeded) throw Fail.Rule(SharedErrorCodes.ValidationFailed, string.Join("; ", created.Errors.Select(e => e.Description)));
        await accounts.SendPasswordResetAsync(user, ct).ConfigureAwait(false);
        return "Staff account created: a set-password link was sent.";
    }
}
