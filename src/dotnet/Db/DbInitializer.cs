using ActualChat.Db.Module;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Db;

public abstract class DbInitializer<TDbContext> : DbServiceBase<TDbContext>, IDbInitializer
    where TDbContext : DbContext
{
    protected DbInfo<TDbContext> DbInfo { get; }

    protected DbInitializer(IServiceProvider services) : base(services)
        => DbInfo = Services.GetRequiredService<DbInfo<TDbContext>>();

    public Dictionary<IDbInitializer, Task> InitializeTasks { get; set; } = null!;

    public virtual async Task Initialize(CancellationToken cancellationToken)
    {
        var dbContext = DbHub.CreateDbContext(readWrite: true);
        await using var _ = dbContext.ConfigureAwait(false);

        var db = dbContext.Database;
        if (db.IsInMemory())
            return;

        if (DbInfo.ShouldRecreateDb) {
            Log.LogInformation("Recreating DB '{DatabaseName}'...", db.GetDbConnection().Database);
            await db.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
            //await db.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            await db.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (DbInfo.ShouldMigrateDb) {
            Log.LogInformation("Migrating DB '{DatabaseName}'...", db.GetDbConnection().Database);
            // var pendingMigrations = await db.GetPendingMigrationsAsync();
            // var appliedMigrations = await db.GetAppliedMigrationsAsync();
            await db.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        else
            await db.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        if (DbInfo.DbKind == DbKind.PostgreSql) {
            var databaseName = db.GetDbConnection().Database;
            await dbContext.Database
                .ExecuteSqlRawAsync(
                    $"ALTER DATABASE \"{databaseName}\" SET DEFAULT_TRANSACTION_ISOLATION TO 'repeatable read';",
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
