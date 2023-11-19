using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

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

    public static Task<DbInitializer<TDbContext>> CompleteEarlierMigrations<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext>(
        this DbInitializer<TDbContext> dbInitializer,
        Migration migration,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        var log = dbInitializer.Services.LogFor(migration.GetType());
        return dbInitializer.CompleteEarlierMigrations(migration.GetId(), log, cancellationToken);
    }

    public static Task<DbInitializer<TDbContext>> CompleteEarlierMigrations<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext>(
        this DbInitializer<TDbContext> dbInitializer,
        Migration migration,
        ILogger log,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
        => dbInitializer.CompleteEarlierMigrations(migration.GetId(), log, cancellationToken);

    public static async Task<DbInitializer<TDbContext>> CompleteEarlierMigrations<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext>(
        this DbInitializer<TDbContext> dbInitializer,
        string migrationId,
        ILogger log,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        var dbContext = dbInitializer.CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var db = dbContext.Database;

        if (db.IsInMemory())
            return dbInitializer;

        var dbInitializerTypeName = dbInitializer.GetType().GetName();
        while (true) {
            try {
                var pendingMigrations = await db
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false);
                var requiredMigrations = pendingMigrations
                    .Where(m => OrdinalCompare(m, migrationId) < 0)
                    .ToList();
                if (requiredMigrations.Count == 0)
                    return dbInitializer;

                log.LogInformation(
                    "Waiting for {DbInitializerType} to complete migrations preceding {MigrationId}: {Migrations}...",
                    dbInitializerTypeName, migrationId, requiredMigrations.ToDelimitedString());
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                log.LogError(e, "GetAppliedMigrationsAsync failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task EnsureCreatedWithMigrationsMarkedAsCompleted(
        this DatabaseFacade db,
        CancellationToken cancellationToken = default)
    {
        // Based on code from Migrator.MigrateAsync
        var historyRepository = db.GetRelationalService<IHistoryRepository>();
        var rawSqlCommandBuilder = db.GetRelationalService<IRawSqlCommandBuilder>();
        var migrationCommandExecutor = db.GetRelationalService<IMigrationCommandExecutor>();
        var connection = db.GetRelationalService<IRelationalConnection>();
        var currentContext = db.GetRelationalService<ICurrentDbContext>();
        var commandLogger = db.GetRelationalService<IRelationalCommandDiagnosticsLogger>();

        // Creating DB
        await db.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        // Creating migration history table
        if (!await historyRepository.ExistsAsync(cancellationToken).ConfigureAwait(false)) {
            var command = rawSqlCommandBuilder.Build(historyRepository.GetCreateScript());
            await command.ExecuteNonQueryAsync(
                    new RelationalCommandParameterObject(
                        connection,
                        null,
                        null,
                        currentContext.Context,
                        commandLogger, CommandSource.Migrations),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // Adding rows to migration history table
        var productVersion = ProductInfo.GetVersion();
        var pendingMigrations = await db.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
        var commands = new List<MigrationCommand>();
        foreach (var migration in pendingMigrations) {
            var historyRow = new HistoryRow(migration, productVersion);
            var insertCommand = rawSqlCommandBuilder.Build(historyRepository.GetInsertScript(historyRow));
            var migrationCommand = new MigrationCommand(insertCommand, currentContext.Context, commandLogger);
            commands.Add(migrationCommand);
        }
        await migrationCommandExecutor
            .ExecuteNonQueryAsync(commands, connection, cancellationToken)
            .ConfigureAwait(false);
    }
}
