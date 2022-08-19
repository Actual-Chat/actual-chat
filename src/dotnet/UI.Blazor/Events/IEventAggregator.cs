namespace ActualChat.UI.Blazor.Events;

public interface IEventAggregator
{
    void Subscribe<TEvent>(EventHandler<TEvent> handler);
    void Unsubscribe<TEvent>(EventHandler<TEvent> handler);
    Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class;
    Task Publish<TEvent>(CancellationToken cancellationToken = default) where TEvent : class, new();

    delegate Task EventHandler<in TEvent>(TEvent @event, CancellationToken cancellationToken);
}
