namespace ActualChat.Core.Server.IntegrationTests.Commands;

public class DedicatedInterfaceEventHandler(ScheduledCommandTestService testService) : ICommandHandler<TestEvent2>
{
    private ScheduledCommandTestService TestService { get; } = testService;

    public Task OnCommand(TestEvent2 eventCommand, CommandContext context, CancellationToken cancellationToken)
    {
        // if (InvalidationMode.IsOn)
        return Task.CompletedTask;

#pragma warning disable CS0162
        return Task.CompletedTask;
        TestService.ProcessedEvents.Enqueue(eventCommand);
        return Task.CompletedTask;
#pragma warning restore CS0162
    }
}
