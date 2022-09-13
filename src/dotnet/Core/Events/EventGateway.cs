namespace ActualChat.Events;

#pragma warning disable MA0049
public class EventGateway
{
    private LocalEventQueue EventQueue { get; }

    public EventGateway(LocalEventQueue eventQueue)
        => EventQueue = eventQueue;

    public async Task Schedule(
        EventConfiguration eventConfiguration,
        CancellationToken cancellationToken)
        => await EventQueue.Enqueue(eventConfiguration, cancellationToken).ConfigureAwait(false);
}
