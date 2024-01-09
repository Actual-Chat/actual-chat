using ActualChat.Commands;
using ActualLab.Time.Testing;

namespace ActualChat.Core.UnitTests.Commands;

public class DedicatedEventHandler(ScheduledCommandTestService testService) : IComputeService
{
    private ScheduledCommandTestService TestService { get; } = testService;

    [EventHandler]
    public virtual async Task OnTestEvent2(TestEvent2 event2, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        using var testClock = new TestClock();
        await testClock.Delay(100, cancellationToken: cancellationToken).ConfigureAwait(false);

        TestService.ProcessedEvents.Enqueue(event2);
    }
}
