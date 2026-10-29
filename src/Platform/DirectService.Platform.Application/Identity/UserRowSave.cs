using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Platform.Application.Identity;

/// <summary>
/// Saves a sign-in change of a user row (DT-222). Simultaneous sign-in attempts on one account change the same row (row version): a correct
/// code arriving together with wrong ones failed with "changed by another user" (409) and the user was not signed in, and wrong attempts at
/// the same moment were not all counted towards the lockout. On a conflict the row is read again and the change applied to its current values;
/// other pending changes of the save (a new session row) and the pending audit action are kept (a failed save keeps both).
/// </summary>
public static class UserRowSave
{
    private const int MaxAttempts = 10;

    public static async Task SaveUserAsync(this IAppDbContext db, User user, Action apply, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            apply();
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                await db.ChangeTracker.Context.Entry(user).ReloadAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
