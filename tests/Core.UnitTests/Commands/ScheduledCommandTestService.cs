using System.Collections.Concurrent;
using ActualChat.Commands;

namespace ActualChat.Core.UnitTests.Commands;

public class ScheduledCommandTestService : IComputeService
{
    public readonly ConcurrentQueue<IEventCommand> ProcessedEvents = new();

    [CommandHandler]
    public virtual Task ProcessTestCommand(TestCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        // Raise events
        new TestEvent(command.Error).EnqueueOnCompletion();
        return Task.CompletedTask;
    }

    [CommandHandler]
    public virtual Task ProcessTestCommand2(TestCommand2 command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        // Raise events
        new TestEvent(null).EnqueueOnCompletion();
        new TestEvent2().EnqueueOnCompletion();
        return Task.CompletedTask;
    }

    [CommandHandler]
    public virtual Task ProcessTestCommand3(TestCommand3 command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        new TestEvent(null)
            .EnqueueOnCompletion();
        new TestEvent2()
            .EnqueueOnCompletion(); // Same as above, actually, but for UserId.None
        return Task.CompletedTask;
    }

    [EventHandler]
    public virtual async Task ProcessTestEvent(TestEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        if (eventCommand.Error != null)
            throw new InvalidOperationException(eventCommand.Error);

        await Task.Delay(250, cancellationToken);
        ProcessedEvents.Enqueue(eventCommand);
    }

    [EventHandler]
    public virtual async Task ProcessTestEvent2(TestEvent2 eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        await Task.Delay(250, cancellationToken);
        ProcessedEvents.Enqueue(eventCommand);
    }
}
