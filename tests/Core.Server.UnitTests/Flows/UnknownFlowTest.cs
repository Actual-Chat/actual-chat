using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.Flows.Internal;
using ActualLab.Resilience;

namespace ActualChat.Core.Server.UnitTests.Flows;

// A resume event of a deleted flow once pinned a CPU core in prod for days: the lookup failure was
// wrapped into a super-transient RetryRequiredException, so the event log retried it forever.
// Both halves of the fix are pinned here - the lookup must fail with a clear error, and that error
// must classify as non-transient, so DbEventForwarder rethrows it and the reader discards the event.
public class UnknownFlowTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void UnknownFlowNameIsRejected()
    {
        var services = new ServiceCollection()
            .AddSingleton(new FlowRegistry())
            .BuildServiceProvider();
        var flowDefs = new FlowDefs(services);

        flowDefs.Invoking(x => x.Get("RemovedFlow")).Should().Throw<KeyNotFoundException>();
        flowDefs.Invoking(x => x.Get(typeof(Flow))).Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void UnknownFlowErrorIsNonTransient()
        => TransiencyResolvers.PreferTransient.Invoke(Errors.UnknownFlow("RemovedFlow"))
            .Should().Be(Transiency.NonTransient);
}
