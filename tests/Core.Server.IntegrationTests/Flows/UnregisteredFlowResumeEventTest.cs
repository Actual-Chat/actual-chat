using ActualChat.Flows;
using ActualChat.Flows.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.LogProcessing;
using ActualLab.Fusion.EntityFramework.Operations;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// Deleting a flow leaves its already-scheduled resume events in _events. They used to fail the
// FlowDefs lookup, get wrapped into a super-transient RetryRequiredException and be retried at the
// speed of a DB round-trip forever - which pinned a core in prod for days. Such an event must now
// exhaust the reader's bounded retries and end up Discarded instead.

[Trait("Category", "Slow")]
public sealed class UnregisteredFlowResumeEventTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(UnregisteredFlowResumeEventTest)}", TestAppHostOptions.Default with {
        ConfigureServices = (_, services) => services.AddFlows(useMasterFlows: false).Add<QuantaFlow>(),
    }, @out)
{
    [Fact(Timeout = 120_000)]
    public async Task AResumeEventOfARemovedFlowIsDiscarded()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        var cancellationToken = cts.Token;

        await using var h = await NewAppHost();
        var flowHub = h.Services.FlowHub();
        var dbHub = h.Services.DbHub<FlowsDbContext>();
        var now = h.Services.Clocks().SystemClock.Now;

        // A FlowResumeEvent can no longer be built for an unregistered flow, so we serialize one for
        // a registered flow and rewrite its FlowId - which is what a row written before the flow's
        // removal deserializes to.
        var arguments = $"removed-{Guid.NewGuid():N}";
        var template = flowHub.NewResumeEvent(flowHub.NewId<QuantaFlow>(arguments));
        var valueJson = DbEvent.Serializer.Write(template, typeof(object))
            .Replace($"{nameof(QuantaFlow)}:{arguments}", $"RemovedFlow:{arguments}", StringComparison.Ordinal);
        valueJson.Should().Contain($"RemovedFlow:{arguments}");

        var uuid = $"FlowResumeEvent(RemovedFlow:{arguments})-at-test";
        var nowDt = now.ToDateTime();
        var pastDt = (now - TimeSpan.FromMinutes(1)).ToDateTime();
        await using (var dbContext = await dbHub.CreateDbContext(true, cancellationToken))
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO _events (uuid, version, state, logged_at, delay_until, value_json)
                VALUES ({uuid}, 1, 0, {nowDt}, {pastDt}, {valueJson})
                """, cancellationToken);

        var state = await WaitForFinalState(dbHub, uuid, cancellationToken);
        state.Should().Be(LogEntryState.Discarded,
            because: "no retry can register a flow that no longer exists");
    }

    // Private methods

    private async Task<LogEntryState> WaitForFinalState(
        DbHub<FlowsDbContext> dbHub, string uuid, CancellationToken cancellationToken)
    {
        while (true) {
            await using (var dbContext = await dbHub.CreateDbContext(cancellationToken)) {
                var entry = await dbContext.Events
                    .FirstOrDefaultAsync(x => x.Uuid == uuid, cancellationToken)
                    .ConfigureAwait(false);
                if (entry is null)
                    throw StandardError.Internal($"Event '{uuid}' is gone.");
                if (entry.State != LogEntryState.New) {
                    WriteLine($"{uuid} -> {entry.State}");
                    return entry.State;
                }
            }
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }
}
