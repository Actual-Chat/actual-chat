using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Db;

public abstract class DbInitializer<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext>
    (IServiceProvider services)
    : DbServiceBase<TDbContext>(services), IDbInitializer
    where TDbContext : DbContext
{
    private const int CommandTimeout = 30;

    private new DbHub<TDbContext> DbHub => base.DbHub;
    public new IServiceProvider Services => base.Services;
    public DbInfo<TDbContext> DbInfo { get; } = services.GetRequiredService<DbInfo<TDbContext>>();
    public HostInfo HostInfo { get; } = services.HostInfo();
    public Dictionary<IDbInitializer, Task> RunningTasks { get; set; } = null!;

    public bool ShouldRepairData => DbInfo.ShouldRepairDb;
    public bool ShouldVerifyData => DbInfo.ShouldVerifyDb;

    public TDbContext CreateDbContext(bool readWrite = false)
    {
        var dbContext = DbHub.ContextFactory.CreateDbContext(default);
        dbContext.ReadWrite(readWrite);

        // Disable auto prepare for statements as we change Collation during migration
        var connectionString = dbContext.Database.GetConnectionString();
        var connectionStringBuilder = new DbConnectionStringBuilder {
            ConnectionString = connectionString,
        };
        const string maxAutoPrepare = "Max Auto Prepare";
        if (connectionStringBuilder.ContainsKey(maxAutoPrepare)
            && !OrdinalEquals((string)connectionStringBuilder[maxAutoPrepare], "0"))
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
                mustMigrate = Random.Shared.Next(30) < 1; // 3% migration probability in tests
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

    protected void ConfigureContext(TDbContext dbContext)
    {
        if (!dbContext.Database.IsInMemory()) {
            dbContext.Database.SetCommandTimeout(CommandTimeout);
        }
    }
}
