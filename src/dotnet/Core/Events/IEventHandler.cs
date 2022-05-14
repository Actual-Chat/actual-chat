namespace ActualChat.Events;

public interface IEventHandler<in T>
    where T: IEvent
{
    Task Handle(T @event, ICommander commander, CancellationToken cancellationToken);
}
