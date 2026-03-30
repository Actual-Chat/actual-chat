using ActualChat.Db.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ActualChat.Db;

public static class DbContextExt
{
    /// <summary>
    /// Temporarily sets command timeout on the DbContext, restoring the original on dispose.
    /// </summary>
    public static ClosedDisposable<(DatabaseFacade Database, int? OriginalTimeout)> WithCommandTimeout(
        this DbContext dbContext, int timeoutInSeconds)
    {
        var database = dbContext.Database;
        var originalTimeout = database.GetCommandTimeout();
        database.SetCommandTimeout(timeoutInSeconds);
        return Disposable.NewClosed(
            (Database: database, OriginalTimeout: originalTimeout),
            state => state.Database.SetCommandTimeout(state.OriginalTimeout));
    }

    // This is a helper method allowing to debug exceptions thrown from SaveChangesAsync
    public static async Task SaveChangesAsync(this DbContext dbContext, ILogger? log, CancellationToken cancellationToken = default)
    {
        try {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            log?.LogError(e, "SaveChangesAsync failed");
            throw;
        }
    }

    // Lock & LockShared

    public static async Task Lock(this DbContext context, DbLockKey key, CancellationToken cancellationToken)
    {
        var database = context.Database;
        var timeout = database.GetCommandTimeout();
        database.SetCommandTimeout(DbSettings.LockTimeout);
        try {
            await database
                .ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key.CombinedKey});", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            var log = StaticLog.For(typeof(DbSetExt));
            log.LogError(ex, "Lock failed for {Key}", key);
            throw;
        }
        finally {
            database.SetCommandTimeout(timeout);
        }
    }

    public static async Task LockShared(this DbContext context, DbLockKey key, CancellationToken cancellationToken)
    {
        var database = context.Database;
        var timeout = database.GetCommandTimeout();
        database.SetCommandTimeout(DbSettings.LockTimeout);
        try {
            await database
                .ExecuteSqlAsync($"SELECT pg_advisory_xact_lock_shared({key.CombinedKey});", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            var log = StaticLog.For(typeof(DbSetExt));
            log.LogError(ex, "Lock failed for {Key}", key);
            throw;
        }
        finally {
            database.SetCommandTimeout(timeout);
        }
    }
}
