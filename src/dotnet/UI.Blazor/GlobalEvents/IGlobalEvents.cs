namespace ActualChat.UI.Blazor;

public interface IGlobalEvents
{
    void Subscribe<TEvent>(GlobalEventHandler<TEvent> handler);
    void Unsubscribe<TEvent>(GlobalEventHandler<TEvent> handler);
    Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class;
    Task Publish<TEvent>(CancellationToken cancellationToken = default) where TEvent : class, new();

}
