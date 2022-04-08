namespace ActualChat.Events;

public interface IEventPublisher
{
    Task Publish<T>(T @event, CancellationToken cancellationToken) where T: class, IEvent;
}

public interface IEventPublisher<T> where T: class, IEvent
{
    Task Publish(T @event, CancellationToken cancellationToken);
}
