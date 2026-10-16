using System.Data;
using System.Data.Common;
using ActualChat.Db.Module;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace ActualChat.Db;

public abstract class DbInitializer<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext>(
    IServiceProvider services
    ) : DbServiceBase<TDbContext>(services), IDbInitializer
    where TDbContext : DbContext
{
    private const int CommandTimeout = 30;
    private const int DropDbCommandTimeout = 120;

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
            await DropDb(db.GetConnectionString().Require(), cancellationToken).ConfigureAwait(false);
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

        // NOTE(AY): We apply migrations one-by-one here, coz EF9 applied all of them in a single transaction,
        // which hid each one's completion from WhenEarlierMigrationsCompleted. EF10+ commits each on its own again.
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

    // Private methods

    private static async Task DropDb(string connectionString, CancellationToken cancellationToken)
    {
        // Not EnsureDeleted: it connects to the DB to check it exists, which fails on a DB a cancelled DROP
        // left invalid, and its command timeout is the DbContext's 30 s, while DROP DATABASE waits for
        // a forced checkpoint that can take longer than that on a loaded CI machine (#5340)
        var builder = new NpgsqlConnectionStringBuilder(connectionString) {
            Pooling = false,
            CommandTimeout = DropDbCommandTimeout,
        };
        var dbName = builder.Database;
        builder.Database = "postgres";
        // As EnsureDeleted does: pooled connections to the dropped DB are dead once FORCE terminates them
        NpgsqlConnection.ClearAllPools();
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await using var _ = connection.ConfigureAwait(false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", connection);
        await using var __ = command.ConfigureAwait(false);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
