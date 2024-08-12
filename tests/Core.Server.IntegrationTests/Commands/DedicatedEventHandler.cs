using ActualLab.Time.Testing;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

public class DedicatedEventHandler(IScheduledCommandTestService testService) : IComputeService
{
    private ScheduledCommandTestService TestService { get; } = (ScheduledCommandTestService)testService;

    [EventHandler]
    public virtual async Task OnTestEvent2(TestEvent2 event2, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        using var testClock = new TestClock();
        await testClock.Delay(100, cancellationToken: cancellationToken).ConfigureAwait(false);

        TestService.ProcessedEvents.Enqueue(event2);
    }
}
