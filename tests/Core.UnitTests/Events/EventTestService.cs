using System.Collections.Concurrent;
using ActualChat.Events;

namespace ActualChat.Core.UnitTests.Events;

public class EventTestService
{
    public readonly ConcurrentQueue<TestEvent> ProcessedEvents = new ();

    [CommandHandler]
    public virtual Task ProcessTestEvent(TestEvent @event, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        ProcessedEvents.Enqueue(@event);
        return Task.CompletedTask;
    }

    [CommandHandler]
    public virtual async Task ProcessTestCommand(TestCommand command, CancellationToken cancellationToken)
        => await new TestEvent()
            .Configure()
            .ScheduleOnCompletion(command)
            .ConfigureAwait(false);

    public record TestEvent : IEvent;
    public record TestCommand : ICommand<Unit>;
}
