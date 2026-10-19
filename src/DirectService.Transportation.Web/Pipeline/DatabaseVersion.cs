using DirectService.Transportation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Web.Pipeline;

/// <summary>
/// The database must have every migration of this version before the host serves requests (owner testing 18 Oct 2026, DT-209: the
/// development database had not received <c>Rev6_PostJournalEntries</c> and <c>Rev6_ReceiptNoSearch</c>, and the trips list failed for
/// every role with "Invalid column name 'ReceiptNoSearch'"). Development applies the missing migrations at start-up and says so in the
/// log; every other environment refuses to start and names them (deployments apply <c>database/MasterDatabase_Idempotent.sql</c>,
/// Setup_and_Run §3). "Testing" is skipped (the API tests recreate their own database). An unreachable database is left to the
/// readiness check, as before.
/// </summary>
internal static partial class DatabaseVersion
{
    public static async Task EnsureCurrentAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.Environment.IsEnvironment("Testing")) return;
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
        List<string> pending;
        try
        {
            pending = (await db.Database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Unreachable(app.Logger, ex);
            return;
        }
        if (pending.Count == 0) return;
        var names = string.Join(", ", pending);
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException(
                $"The database is behind this version of the application: {pending.Count} migration(s) not applied ({names}). " +
                "Apply database/MasterDatabase_Idempotent.sql (docs/step9/Setup_and_Run.md) and start the application again.");
        Applying(app.Logger, pending.Count, names);
        await db.Database.MigrateAsync().ConfigureAwait(false);
        Applied(app.Logger, pending.Count);
    }

    [LoggerMessage(EventId = 9101, Level = LogLevel.Warning, Message = "The development database is behind this version: applying {Count} migration(s): {Names}")]
    private static partial void Applying(ILogger logger, int count, string names);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Information, Message = "Applied {Count} migration(s) to the development database.")]
    private static partial void Applied(ILogger logger, int count);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Warning, Message = "The database could not be read to check its version; the readiness check reports it.")]
    private static partial void Unreachable(ILogger logger, Exception ex);
}
