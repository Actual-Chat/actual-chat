using ActualChat.Db.Module;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Db;

public abstract class DbInitializer<TDbContext> : DbServiceBase<TDbContext>, IDbInitializer
    where TDbContext : DbContext
{
    public new IServiceProvider Services => base.Services;
    public new DbHub<TDbContext> DbHub => base.DbHub;
    public DbInfo<TDbContext> DbInfo { get; }
    public HostInfo HostInfo { get; }
    public Dictionary<IDbInitializer, Task> InitializeTasks { get; set; } = null!;

    protected DbInitializer(IServiceProvider services) : base(services)
    {
        DbInfo = services.GetRequiredService<DbInfo<TDbContext>>();
        HostInfo = services.GetRequiredService<HostInfo>();
    }

    public virtual async Task Initialize(CancellationToken cancellationToken)
    {
        var hostInfo = Services.GetRequiredService<HostInfo>();

        var dbContext = DbHub.CreateDbContext(readWrite: true);
        await using var _ = dbContext.ConfigureAwait(false);

        var db = dbContext.Database;
        if (db.IsInMemory())
            goto initializeData;

        var dbName = db.GetDbConnection().Database;
        if (DbInfo.ShouldRecreateDb) {
            Log.LogInformation("Recreating DB '{DatabaseName}'...", dbName);
            await db.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
            var mustMigrate = false;
            if (hostInfo.AppKind.IsTestServer())
                mustMigrate = Random.Shared.Next(10) < 1; // 10% migration probability in tests
            if (mustMigrate)
                await db.MigrateAsync(cancellationToken).ConfigureAwait(false);
            else
                await db.EnsureCreatedWithMigrationsMarkedAsCompleted(cancellationToken).ConfigureAwait(false);
        }
        else if (DbInfo.ShouldMigrateDb) {
            var migrations = (await db
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false)
                ).ToList();
            if (migrations.Count != 0) {
                Log.LogInformation(
                    "Migrating DB '{DatabaseName}': applying {Migrations}...",
                    dbName, migrations.ToDelimitedString());
                await db.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            else
                Log.LogInformation(
                    "Migrating DB '{DatabaseName}': no migrations to apply", dbName);
        }
        else
            throw StandardError.Internal("Either DbInfo.ShouldRecreateDb or ShouldMigrateDb must be true.");

        if (DbInfo.DbKind == DbKind.PostgreSql) {
            var databaseName = dbName;
            await dbContext.Database
                .ExecuteSqlRawAsync(
                    $"ALTER DATABASE \"{databaseName}\" SET DEFAULT_TRANSACTION_ISOLATION TO 'repeatable read';",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        initializeData:

        await InitializeData(cancellationToken).ConfigureAwait(false);
        if (DbInfo.ShouldVerifyDb)
            await VerifyData(cancellationToken).ConfigureAwait(false);
    }

    protected virtual Task InitializeData(CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected virtual Task VerifyData(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
