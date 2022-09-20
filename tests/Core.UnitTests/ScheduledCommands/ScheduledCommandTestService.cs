using System.Collections.Concurrent;
using ActualChat.ScheduledCommands;

namespace ActualChat.Core.UnitTests.ScheduledCommands;

public class ScheduledCommandTestService
{
    public readonly ConcurrentQueue<TestEvent> ProcessedEvents = new ();

    [CommandHandler]
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

    public record TestEvent(string? Error) : IEvent;
    public record TestCommand(string? Error) : ICommand<Unit>;
}
