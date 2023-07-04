using ActualChat.Db.Module;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Db;

public abstract class DbInitializer<TDbContext> : DbServiceBase<TDbContext>, IDbInitializer
    where TDbContext : DbContext
{
    private const int CommandTimeout = 30;

    private new DbHub<TDbContext> DbHub => base.DbHub;
    public new IServiceProvider Services => base.Services;
    public DbInfo<TDbContext> DbInfo { get; }
    public HostInfo HostInfo { get; }
    public Dictionary<IDbInitializer, Task> RunningTasks { get; set; } = null!;

    public bool ShouldRepairData => DbInfo.ShouldRepairDb;
    public bool ShouldVerifyData => DbInfo.ShouldVerifyDb;

    protected DbInitializer(IServiceProvider services) : base(services)
    {
        DbInfo = services.GetRequiredService<DbInfo<TDbContext>>();
        HostInfo = services.GetRequiredService<HostInfo>();
    }

    public new TDbContext CreateDbContext(bool readWrite = false)
    {
        var dbContext = DbHub.CreateDbContext(readWrite);
        ConfigureContext(dbContext);
        return dbContext;
    }

    public virtual async Task InitializeSchema(CancellationToken cancellationToken)
    {
        var hostInfo = Services.GetRequiredService<HostInfo>();

        var dbContext = CreateDbContext(readWrite: true);
        await using var _ = dbContext.ConfigureAwait(false);

        var db = dbContext.Database;
        if (db.IsInMemory())
            return;

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
    }

    public virtual Task InitializeData(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public virtual Task RepairData(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public virtual Task VerifyData(CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected void ConfigureContext(TDbContext dbContext)
        => dbContext.Database.SetCommandTimeout(CommandTimeout);
}
