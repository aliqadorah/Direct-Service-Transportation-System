using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Identity;

/// <summary>
/// Authenticator codes that work once (owner decision 19 Oct 2026, DT-212). Identity's own provider accepts the same code again for as
/// long as it is in its time window, so a code seen on a screen or over a shoulder could be used a second time. Here a code is accepted
/// only for a time step later than the user's last accepted one, and the step is claimed with one conditional update
/// (<c>WHERE TwoFactorLastStep IS NULL OR TwoFactorLastStep &lt; @step</c>): of two requests with the same code — or with two codes,
/// the older second — only one succeeds. Same algorithm and window as before for a fresh code (RFC 6238, ±1 step: <see cref="TotpCode"/>).
/// Registered as the authenticator token provider, so sign-in, set-up and the profile's checks all use it.
/// </summary>
public sealed class ReplaySafeAuthenticatorTokenProvider(MasterDbContext db, TimeProvider clock) : IUserTwoFactorTokenProvider<User>
{
    public const string Name = "ReplaySafeAuthenticator";
    private static readonly string[] AllFilters = ["Tenant", "SoftDelete"];

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<User> manager, User user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false));
    }

    /// <summary>Codes come from the user's app, never from the server.</summary>
    public Task<string> GenerateAsync(string purpose, UserManager<User> manager, User user) => Task.FromResult(string.Empty);

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<User> manager, User user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);
        var key = await manager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (TotpCode.MatchStep(key, token, clock.GetUtcNow()) is not long step) return false;
        // One statement claims the step and returns the row version before and after, so the caller's own save of this user (e.g.
        // enabling 2FA right after its first code) does not meet a false "changed by another user".
        var claimed = await db.Database.SqlQuery<RowVersions>(
                $"UPDATE [core].[Users] SET [TwoFactorLastStep] = {step} OUTPUT deleted.[RowVersion] AS [Before], inserted.[RowVersion] AS [After] WHERE [Id] = {user.Id} AND ([TwoFactorLastStep] IS NULL OR [TwoFactorLastStep] < {step})")
            .ToListAsync().ConfigureAwait(false);
        if (claimed.Count != 1) return false;   // this code (or a later one) was used already
        var entry = db.Entry(user);
        if (entry.State != EntityState.Detached)
        {
            entry.Property(x => x.TwoFactorLastStep).CurrentValue = step;
            entry.Property(x => x.TwoFactorLastStep).OriginalValue = step;   // stored already; not written again
            // Move the tracked version forward only if nothing else changed the row meanwhile (a real concurrent change still conflicts).
            if (entry.Property(x => x.RowVersion).OriginalValue.AsSpan().SequenceEqual(claimed[0].Before))
            {
                entry.Property(x => x.RowVersion).OriginalValue = claimed[0].After;
                entry.Property(x => x.RowVersion).CurrentValue = claimed[0].After;
            }
        }
        else user.TwoFactorLastStep = step;
        return true;
    }

    private sealed record RowVersions(byte[] Before, byte[] After);
}
