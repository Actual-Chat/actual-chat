using System.Data;
using System.Data.Common;
using ActualChat.Db.Module;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ActualChat.Db;

public abstract class DbInitializer<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext>(
    IServiceProvider services
    ) : DbServiceBase<TDbContext>(services), IDbInitializer
    where TDbContext : DbContext
{
    private const int CommandTimeout = 30;

    private new DbHub<TDbContext> DbHub => base.DbHub;
    public new IServiceProvider Services => base.Services;
    public DbInfo<TDbContext> DbInfo => field ??= Services.GetRequiredService<DbInfo<TDbContext>>();
    public HostInfo HostInfo => field ??= Services.HostInfo();
    public Dictionary<IDbInitializer, Task> RunningTasks { get; set; } = null!;

    public bool ShouldRepairData => DbInfo.ShouldRepairDb;
    public bool ShouldVerifyData => DbInfo.ShouldVerifyDb;

    public TDbContext CreateDbContext(bool readWrite = false)
    {
        var dbContext = DbHub.ContextFactory.CreateDbContext(DbShard.Single);
        dbContext.ReadWrite(readWrite);

        // Disable auto prepare for statements as we change Collation during migration
        var connectionString = dbContext.Database.GetConnectionString();
        var connectionStringBuilder = new DbConnectionStringBuilder {
            ConnectionString = connectionString,
        };
        const string maxAutoPrepare = "Max Auto Prepare";
        if (connectionStringBuilder.ContainsKey(maxAutoPrepare)
            && (string)connectionStringBuilder[maxAutoPrepare] != "0")
            connectionStringBuilder[maxAutoPrepare] = "0";
        if (dbContext.Database.GetDbConnection().State == ConnectionState.Closed)
            dbContext.Database.SetConnectionString(connectionStringBuilder.ConnectionString);

        ConfigureContext(dbContext);
        return dbContext;
    }

    public virtual async Task InitializeSchema(CancellationToken cancellationToken)
    {
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
            if (HostInfo.IsTested)
                mustMigrate = Random.Shared.Next(100) < 3; // 3% migration probability in tests
            if (mustMigrate)
                await MigrateDb(db, cancellationToken).ConfigureAwait(false);
            else
                await db.EnsureCreatedWithMigrationsMarkedAsCompleted(cancellationToken).ConfigureAwait(false);
        }
        else if (DbInfo.ShouldMigrateDb)
            await MigrateDb(db, cancellationToken).ConfigureAwait(false);
        else
            throw StandardError.Internal("Either DbInfo.ShouldRecreateDb or ShouldMigrateDb must be true.");

        if (DbInfo.DbKind == DbKind.PostgreSql) {
            var databaseName = dbName;
#pragma warning disable EF1002
            await dbContext.Database
                .ExecuteSqlRawAsync(
                    $"ALTER DATABASE \"{databaseName}\" SET DEFAULT_TRANSACTION_ISOLATION TO 'repeatable read';",
                    cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore EF1002
        }
    }

    public virtual Task InitializeData(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public virtual Task RepairData(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public virtual Task VerifyData(CancellationToken cancellationToken)
        => Task.CompletedTask;

    // Protected methods

    protected void ConfigureContext(TDbContext dbContext)
    {
        if (!dbContext.Database.IsInMemory())
            dbContext.Database.SetCommandTimeout(CommandTimeout);
    }

    protected async Task MigrateDb(DatabaseFacade db, CancellationToken cancellationToken)
    {
        var dbName = db.GetDbConnection().Database;
        if (db.IsInMemory()) {
            Log.LogInformation("MigrateDb '{DatabaseName}': no migrations to apply (in-memory DB)", dbName);
            return;
        }

        var migrations = (await db
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false)
            ).ToList();
        if (migrations.Count == 0) {
            Log.LogInformation("MigrateDb '{DatabaseName}': no migrations to apply", dbName);
            return;
        }

        // NOTE(AY): We apply migrations one-by-one here, coz EF9+ applies all of them in a single transaction,
        // and such a behavior doesn't allow WhenEarlierMigrationsCompleted to detect the completion properly.
        Log.LogInformation("MigrateDb '{DatabaseName}': applying {Count} migration(s):", dbName, migrations.Count);
        foreach (var migration in migrations) {
            Log.LogInformation("- MigrateDb '{DatabaseName}': applying '{Migration}'", dbName, migration);
            try {
                await db.MigrateAsync(migration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogError(e, "MigrateDb '{DatabaseName}' failed on '{Migration}'", dbName, migration);
                throw;
            }
        }
        Log.LogInformation("MigrateDb '{DatabaseName}': completed", dbName);
    }
}
