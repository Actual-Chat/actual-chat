namespace ActualChat.Events;

public interface IEventHandler<T> where T: IEvent
{
    Task Handle(T @event, ICommander commander, CancellationToken cancellationToken);
}
