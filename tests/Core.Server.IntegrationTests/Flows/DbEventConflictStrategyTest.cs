using System.Collections.Concurrent;
using System.Data.Common;
using ActualChat.Db;
using ActualChat.Flows.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.LogProcessing;
using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Trait("Category", "Slow")]
public sealed class DbEventConflictStrategyTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(DbEventConflictStrategyTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task DbEventInsertUsesOnConflictDoNothing()
    {
        // arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var cancellationToken = cts.Token;

        await using var h = await NewAppHost();
        var dbHub = h.Services.DbHub<FlowsDbContext>();

        string connectionString;
        await using (var dbContext = await dbHub.CreateDbContext(true, cancellationToken))
            connectionString = dbContext.Database.GetConnectionString()!;

        var sqlSink = new SqlCapturingInterceptor();
        var optionsBuilder = new DbContextOptionsBuilder<FlowsDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(sqlSink);
        optionsBuilder.UseNpgsqlConflictStrategies(); // Mutates in place; returns the non-generic builder
        var options = optionsBuilder.Options;

        var uuid = $"FlowResumeEvent({nameof(DbEventConflictStrategyTest)}:{Guid.NewGuid():N})-at-test";
        var now = DateTime.UtcNow;
        var future = now + TimeSpan.FromDays(1);

        // act
        await using (var db = new FlowsDbContext(options)) {
            db.Events.Add(NewDbEvent(uuid, version: 1, now, future));
            await db.SaveChangesAsync(cancellationToken);
        }
        // Re-inserting the same Uuid (the concurrent-schedule race) leaves the row untouched, so
        // EF sees 0 affected rows and raises a benign, transient DbUpdateConcurrencyException -
        // never a 23505 unique_violation.
        Exception? reinsertError;
        await using (var db = new FlowsDbContext(options)) {
            db.Events.Add(NewDbEvent(uuid, version: 2, now, future));
            reinsertError = await Record.ExceptionAsync(() => db.SaveChangesAsync(cancellationToken));
        }
        List<DbEvent> rows;
        await using (var db = new FlowsDbContext(options))
            rows = await db.Events.Where(e => e.Uuid == uuid).ToListAsync(cancellationToken);

        // assert
        sqlSink.Commands.Any(IsEventsInsertWithDoNothing).Should()
            .BeTrue("the _events INSERT must carry ON CONFLICT DO NOTHING; captured: "
                + string.Join(" || ", sqlSink.Commands));
        HasUniqueViolation(reinsertError).Should()
            .BeFalse($"duplicate _events insert must not raise 23505, but got: {reinsertError}");
        rows.Count.Should().Be(1);
        rows[0].Version.Should().Be(1); // DoNothing kept the original row, not the re-insert
    }

    private static bool IsEventsInsertWithDoNothing(string sql)
        => sql.Contains("_events", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("ON CONFLICT DO NOTHING", StringComparison.OrdinalIgnoreCase);

    private static bool HasUniqueViolation(Exception? error)
        => error switch {
            null => false,
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => true,
            AggregateException agg => agg.InnerExceptions.Any(HasUniqueViolation),
            _ => HasUniqueViolation(error.InnerException),
        };

    private static DbEvent NewDbEvent(string uuid, long version, DateTime loggedAt, DateTime delayUntil)
        => new() {
            Uuid = uuid,
            Version = version,
            State = LogEntryState.New,
            LoggedAt = loggedAt,
            DelayUntil = delayUntil,
            ValueJson = "{}",
        };

    private sealed class SqlCapturingInterceptor : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken)
        {
            Commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken)
        {
            Commands.Enqueue(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
