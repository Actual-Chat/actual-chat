using ActualChat.Commands;

namespace ActualChat.Core.UnitTests.ScheduledCommands;

public class DedicatedInterfaceEventHandler : IEventHandler<TestEvent2>
{
    private ScheduledCommandTestService TestService { get; }

    public DedicatedInterfaceEventHandler(ScheduledCommandTestService testService)
        => TestService = testService;

    public virtual Task OnEvent(TestEvent2 @event, CommandContext context, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        throw new InvalidOperationException("Should not run!");
 #pragma warning disable CS0162
        TestService.ProcessedEvents.Enqueue(@event);
        return Task.CompletedTask;
 #pragma warning restore CS0162
    }
}
