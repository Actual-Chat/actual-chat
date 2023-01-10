namespace ActualChat.Core.UnitTests.Commands;

public class DedicatedInterfaceEventHandler : ICommandHandler<TestEvent2>
{
    private ScheduledCommandTestService TestService { get; }

    public DedicatedInterfaceEventHandler(ScheduledCommandTestService testService)
        => TestService = testService;

    public Task OnCommand(TestEvent2 eventCommand, CommandContext context, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        return Task.CompletedTask;
 #pragma warning disable CS0162
        TestService.ProcessedEvents.Enqueue(eventCommand);
        return Task.CompletedTask;
 #pragma warning restore CS0162
    }
}
