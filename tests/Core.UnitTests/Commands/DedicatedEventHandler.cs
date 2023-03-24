using ActualChat.Commands;
using Stl.Time.Testing;

namespace ActualChat.Core.UnitTests.Commands;

public class DedicatedEventHandler : IComputeService
{
    private ScheduledCommandTestService TestService { get; }

    public DedicatedEventHandler(ScheduledCommandTestService testService)
        => TestService = testService;

    [EventHandler]
    public virtual async Task OnTestEvent2(TestEvent2 event2, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var testClock = new TestClock();
        await testClock.Delay(100, cancellationToken: cancellationToken).ConfigureAwait(false);

        TestService.ProcessedEvents.Enqueue(event2);
    }
}
