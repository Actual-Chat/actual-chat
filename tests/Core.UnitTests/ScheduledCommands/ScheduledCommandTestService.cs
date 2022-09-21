using System.Collections.Concurrent;
using ActualChat.ScheduledCommands;

namespace ActualChat.Core.UnitTests.ScheduledCommands;

public class ScheduledCommandTestService
{
    public readonly ConcurrentQueue<IEvent> ProcessedEvents = new ();

    [EventHandler]
    public virtual Task ProcessTestEvent2(TestEvent2 @event, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        ProcessedEvents.Enqueue(@event);
        return Task.CompletedTask;
    }

    [EventHandler]
    public virtual Task ProcessTestEvent(TestEvent @event, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        if (@event.Error != null)
            throw new InvalidOperationException(@event.Error);

        ProcessedEvents.Enqueue(@event);
        return Task.CompletedTask;
    }

    [CommandHandler]
    public virtual async Task ProcessTestCommand(TestCommand command, CancellationToken cancellationToken)
        => await new TestEvent(command.Error)
            .Configure()
            .ScheduleOnCompletion(command, cancellationToken)
            .ConfigureAwait(false);

    [CommandHandler]
    public virtual async Task ProcessTestCommand2(TestCommand2 command, CancellationToken cancellationToken)
        => await new TestEvent2()
            .ScheduleOnCompletion(command, cancellationToken)
            .ConfigureAwait(false);

}
