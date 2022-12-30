using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ActualChat.Db;

public static class MigrationExt
{
    public static string GetId(this Migration migration)
    {
        var type = migration.GetType();
        var attribute = type.GetCustomAttribute<MigrationAttribute>()
            ?? throw StandardError.Internal($"Type {type.GetName()} doesn't have [Migration] attribute.");
        return attribute.Id;
    }

    public static Task<DbInitializer<TDbContext>> CompleteEarlierMigrations<TDbContext>(
        this DbInitializer<TDbContext> dbInitializer,
        Migration migration,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        var log = dbInitializer.Services.LogFor(migration.GetType());
        return dbInitializer.CompleteEarlierMigrations(migration.GetId(), log, cancellationToken);
    }

    public static Task<DbInitializer<TDbContext>> CompleteEarlierMigrations<TDbContext>(
        this DbInitializer<TDbContext> dbInitializer,
        Migration migration,
        ILogger log,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
        => dbInitializer.CompleteEarlierMigrations(migration.GetId(), log, cancellationToken);

    public static async Task<DbInitializer<TDbContext>> CompleteEarlierMigrations<TDbContext>(
        this DbInitializer<TDbContext> dbInitializer,
        string migrationId,
        ILogger log,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        var dbContext = dbInitializer.DbHub.CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var database = dbContext.Database;

        var dbInitializerTypeName = dbInitializer.GetType().GetName();
        while (true) {
            var pendingMigrations = await database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            if (pendingMigrations.All(m => OrdinalCompare(m, migrationId) >= 0))
                break;

            log.LogInformation("Waiting for {DbInitializerType} to complete all migrations < '{MigrationId}'",
                dbInitializerTypeName, migrationId);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        return dbInitializer;
    }
}
