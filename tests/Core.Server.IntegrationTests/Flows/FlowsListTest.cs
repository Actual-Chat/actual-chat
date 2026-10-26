using ActualChat.Flows;
using ActualChat.Flows.Db;
using ActualChat.Flows.Infrastructure;
using ActualChat.Testing.Host;
using ActualChat.Users.Flows;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;
using FlowData = ActualChat.Flows.FlowData;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Trait("Category", "Slow")]
public class FlowsListTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(FlowsListTest)}", TestAppHostOptions.Default, @out)
{
    private TestAppHost AppHost { get; set; } = null!;

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Done here to keep the host's startup out of the test's timeout. ListStats and List also
        // wait for the flow backend's shard, which a fresh host takes a while to own.
        // xUnit runs this before every test, so a second test here should move the host to a fixture.
        AppHost = await NewAppHost();
        await AppHost.Services.WhenFlowsStarted();
    }

    protected override async Task DisposeAsync()
    {
        await AppHost.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 60_000)]
    public async Task ListReportsStatuses()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var cancellationToken = cts.Token;

        var services = AppHost.Services;
        var now = services.Clocks().SystemClock.Now;

        // FlowBackend.List derives UpdatedAt from Version, assuming Version == clock-based epoch ticks.
        var backend = services.GetRequiredService<IFlowBackend>();
        var dbHub = services.DbHub<FlowsDbContext>();

        var ticks = now.EpochOffset.Ticks;
        // DigestFlow sets MaxResumeGap to 3 days, so only one stored before that is checked
        var oldTicks = (now - TimeSpan.FromDays(4)).EpochOffset.Ticks;
        var prefix = $"FlowsListTest{Guid.NewGuid():N}";
        // The synthetic {prefix}A flows are not registered, so nothing tells they are stuck
        var suspId = $"{prefix}A:susp";
        var legacySuspId = $"{prefix}A:legacy-susp";
        var lostId = $"DigestFlow:{prefix}-lost";
        var unreadableId = $"DigestFlow:{prefix}-unreadable";
        var parkedId = $"DigestFlow:{prefix}-parked";
        var waitingId = $"DigestFlow:{prefix}-waiting";
        var freshId = $"DigestFlow:{prefix}-fresh";
        var seededIds = new[] {
            $"{prefix}A:ok", $"{prefix}A:bad", $"{prefix}A:idle", suspId, legacySuspId,
            lostId, unreadableId, parkedId, waitingId, freshId,
        };
        var seed = new[] {
            NewDbFlow($"{prefix}A:ok", ticks, isCompleted: true, isFailed: false),
            NewDbFlow($"{prefix}A:bad", ticks, isCompleted: true, isFailed: true),
            NewDbFlow($"{prefix}A:idle", ticks, isCompleted: false, isFailed: false),
            NewDbFlow(suspId, ticks, isCompleted: false, isFailed: false),
            NewDbFlow(legacySuspId, ticks, isCompleted: false, isFailed: false),
            NewDigestDbFlow(lostId, oldTicks, now - TimeSpan.FromDays(3)),
            NewDbFlow(unreadableId, oldTicks, isCompleted: false, isFailed: false),
            NewDigestDbFlow(parkedId, oldTicks, Moment.MaxValue),
            NewDigestDbFlow(waitingId, oldTicks, now + TimeSpan.FromDays(1)),
            NewDigestDbFlow(freshId, ticks, now - TimeSpan.FromDays(3)),
        };
        await using (var dbContext = await dbHub.CreateDbContext(true, cancellationToken)) {
            dbContext.Flows.AddRange(seed);
            await dbContext.SaveChangesAsync(cancellationToken);

            // A pending resume event makes {prefix}A:susp count as Scheduled - an own one here, and an
            // external one in the format events had before "r(...)" for {prefix}A:legacy-susp.
            // delay_until must be in the future, otherwise the AppHost's DbEventProcessor picks it
            // up mid-test and the event stops being pending.
            var suspUuid = $"r({suspId})-self-at-test";
            var legacySuspUuid = $"FlowResumeEvent({legacySuspId})-at-test";
            // The stuck check reads the event itself, so this one carries a real one
            var waitingUuid = $"r({waitingId})-self-at-test";
            var waitingResumeEvent = services.FlowHub().NewResumeEvent(new FlowId(waitingId)).WithSelfResume();
            var waitingJson = DbLogEntrySerializer.Default.TextSerializer.Write(waitingResumeEvent, typeof(object));
            var nowDt = now.ToDateTime();
            var futureDt = (now + TimeSpan.FromDays(1)).ToDateTime();
            var emptyJson = "{}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO _events (uuid, version, state, logged_at, delay_until, value_json)
                VALUES ({suspUuid}, 1, 0, {nowDt}, {futureDt}, {emptyJson}),
                       ({legacySuspUuid}, 1, 0, {nowDt}, {futureDt}, {emptyJson}),
                       ({waitingUuid}, 1, 0, {nowDt}, {futureDt}, {waitingJson})
                """, cancellationToken);
        }

        var stats = await backend.ListStats(default, cancellationToken);

        var aggA = stats.Single(a => a.Name == $"{prefix}A");
        aggA.Completed.Should().Be(2, "a failed flow is completed too");
        aggA.Failed.Should().Be(1);
        aggA.Scheduled.Should().Be(2);
        aggA.Idle.Should().Be(1);
        aggA.MaybeStuck.Should().Be(0, "the type sets no MaxResumeGap");
        var aggDigest = stats.Single(a => a.Name == "DigestFlow");
        aggDigest.MaybeStuck.Should().BeGreaterThanOrEqualTo(3,
            "the lost, unreadable and parked flows are idle past the gap, whatever their own check says");

        var allQuery = new FlowsQuery(Limit: 10_000, Scope: FlowListScope.All);
        var allRows = await backend.List(default, allQuery, cancellationToken);
        var rowById = allRows.Where(r => seededIds.Contains(r.FlowId)).ToDictionary(r => r.FlowId);
        rowById[$"{prefix}A:ok"].Status.Should().Be(FlowStatus.Completed);
        rowById[$"{prefix}A:bad"].Status.Should().Be(FlowStatus.Failed);
        rowById[$"{prefix}A:idle"].Status.Should().Be(FlowStatus.Idle);
        rowById[suspId].Status.Should().Be(FlowStatus.Scheduled);
        rowById[legacySuspId].Status.Should().Be(FlowStatus.Scheduled);
        rowById[lostId].Status.Should().Be(FlowStatus.Stuck, "its next run is due and no resume is pending");
        rowById[unreadableId].Status.Should().Be(FlowStatus.Stuck, "it can't tell, and no resume is pending");
        rowById[parkedId].Status.Should().Be(FlowStatus.Idle, "it waits for an external resume");
        rowById[waitingId].Status.Should().Be(FlowStatus.Scheduled, "its next run is pending");
        rowById[freshId].Status.Should().Be(FlowStatus.Idle, "it resumed within MaxResumeGap");

        var problematicQuery = new FlowsQuery(Limit: 10_000, Scope: FlowListScope.Problematic);
        var problematic = await backend.List(default, problematicQuery, cancellationToken);
        var ours = problematic.Where(r => seededIds.Contains(r.FlowId)).ToList();
        ours.Select(r => r.FlowId).Should().BeEquivalentTo([$"{prefix}A:bad", lostId, unreadableId]);

        var endedQuery = new FlowsQuery(Limit: 10_000, Scope: FlowListScope.Ended);
        var ended = await backend.List(default, endedQuery, cancellationToken);
        ended.Where(r => seededIds.Contains(r.FlowId)).Select(r => r.FlowId).Should()
            .BeEquivalentTo([$"{prefix}A:ok", $"{prefix}A:bad"]);

        var typeAQuery = new FlowsQuery($"{prefix}A", 10_000, FlowListScope.All);
        var typeA = await backend.List(default, typeAQuery, cancellationToken);
        typeA.Should().OnlyContain(r => r.Name == $"{prefix}A");
        typeA.Select(r => r.FlowId).Should()
            .BeEquivalentTo([$"{prefix}A:ok", $"{prefix}A:bad", $"{prefix}A:idle", suspId, legacySuspId]);
    }

    // Private methods

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

    private static DbFlow NewDigestDbFlow(string id, long version, Moment nextRunAt)
    {
        var flow = new DigestFlow { NextRunAt = nextRunAt };
        var flowImpl = (IFlowImpl)flow;
        flowImpl.SetProperties(new FlowId(id), version, 1, null, new FlowConsole(flow));
        return new DbFlow(FlowData.FromFlow(flow));
    }
}
