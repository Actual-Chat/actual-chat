using ActualChat.Flows;
using ActualChat.Flows.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Trait("Category", "Slow")]
public class FlowsListTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(FlowsListTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 180_000)]
    public async Task ListReportsStatuses()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(170));
        var cancellationToken = cts.Token;

        await using var h = await NewAppHost();
        var clock = h.Services.Clocks().SystemClock;
        var now = clock.Now;

        // FlowBackend.List derives UpdatedAt from Version, assuming Version == clock-based epoch ticks.
        var backend = h.Services.GetRequiredService<IFlowBackend>();
        var dbHub = h.Services.DbHub<FlowsDbContext>();

        var ticks = now.EpochOffset.Ticks;
        var prefix = $"FlowsListTest{Guid.NewGuid():N}";
        // DigestFlow is a registered PeriodicFlow, so a non-completed one without a pending resume
        // event is classified as Stuck. The synthetic {prefix}A flows are not registered, hence Idle.
        var stuckId = $"DigestFlow:{prefix}";
        var suspId = $"{prefix}A:susp";
        var seededIds = new[] { $"{prefix}A:ok", $"{prefix}A:bad", $"{prefix}A:idle", suspId, stuckId };
        var seed = new[] {
            NewDbFlow($"{prefix}A:ok", ticks, isCompleted: true, isFailed: false),
            NewDbFlow($"{prefix}A:bad", ticks, isCompleted: true, isFailed: true),
            NewDbFlow($"{prefix}A:idle", ticks, isCompleted: false, isFailed: false),
            NewDbFlow(suspId, ticks, isCompleted: false, isFailed: false),
            NewDbFlow(stuckId, ticks, isCompleted: false, isFailed: false),
        };
        await using (var dbContext = await dbHub.CreateDbContext(true, cancellationToken)) {
            dbContext.Flows.AddRange(seed);
            await dbContext.SaveChangesAsync(cancellationToken);

            // A pending resume event makes {prefix}A:susp count as Suspended. delay_until must be
            // in the future, otherwise the AppHost's DbEventProcessor picks it up mid-test and the
            // event stops being pending.
            var suspUuid = $"FlowResumeEvent({suspId})-at-test";
            var nowDt = now.ToDateTime();
            var futureDt = (now + TimeSpan.FromDays(1)).ToDateTime();
            var emptyJson = "{}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO _events (uuid, version, state, logged_at, delay_until, value_json)
                VALUES ({suspUuid}, 1, 0, {nowDt}, {futureDt}, {emptyJson})
                """, cancellationToken);
        }

        var stats = await backend.ListStats(default, cancellationToken);

        var aggA = stats.Single(a => a.Name == $"{prefix}A");
        aggA.Completed.Should().Be(1);
        aggA.Failed.Should().Be(1);
        aggA.Suspended.Should().Be(1);
        aggA.Idle.Should().Be(1);
        aggA.Stuck.Should().Be(0);

        var allRows = await backend.List(default, new FlowsQuery(Limit: 10_000), cancellationToken);
        var rowById = allRows.Where(r => seededIds.Contains(r.FlowId)).ToDictionary(r => r.FlowId);
        rowById[$"{prefix}A:ok"].Status.Should().Be(FlowStatus.Completed);
        rowById[$"{prefix}A:bad"].Status.Should().Be(FlowStatus.Failed);
        rowById[$"{prefix}A:idle"].Status.Should().Be(FlowStatus.Idle);
        rowById[suspId].Status.Should().Be(FlowStatus.Suspended);
        rowById[stuckId].Status.Should().Be(FlowStatus.Stuck);

        var problematic = await backend.List(default, new FlowsQuery(ProblematicOnly: true, Limit: 10_000), cancellationToken);
        var ours = problematic.Where(r => seededIds.Contains(r.FlowId)).ToList();
        ours.Select(r => r.FlowId).Should().BeEquivalentTo([$"{prefix}A:bad", stuckId]);

        var typeA = await backend.List(default, new FlowsQuery(Name: $"{prefix}A", Limit: 10_000), cancellationToken);
        typeA.Should().OnlyContain(r => r.Name == $"{prefix}A");
        typeA.Select(r => r.FlowId).Should()
            .BeEquivalentTo([$"{prefix}A:ok", $"{prefix}A:bad", $"{prefix}A:idle", suspId]);
    }

    private static DbFlow NewDbFlow(string id, long version, bool isCompleted, bool isFailed)
        => new() {
            Id = id,
            Version = version,
            DataVersion = 1,
            IsCompleted = isCompleted,
            IsFailed = isFailed,
            Data = [1],
            ResultData = isCompleted ? [1] : null,
            Console = "",
        };
}
