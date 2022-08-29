namespace ActualChat.UI.Blazor;

public sealed class UIEventHub
{
    private readonly Dictionary<Type, ImmutableList<object>> _handlers = new ();

    public void Subscribe<TEvent>(UIEventHandler<TEvent> handler)
    {
        lock(_handlers)
            _handlers[typeof(TEvent)] = _handlers.GetValueOrDefault(typeof(TEvent))?.Add(handler) ?? ImmutableList.Create<object>(handler);
    }

    public void Unsubscribe<TEvent>(UIEventHandler<TEvent> handler)
    {
        lock (_handlers) {
            var eventHandlers = _handlers[typeof(TEvent)];
            var updatedList = eventHandlers.Remove(handler);
            if (updatedList.Count == 0)
                _handlers.Remove(typeof(TEvent));
            else
                _handlers[typeof(TEvent)] = eventHandlers;
        }
    }

    public async Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class
    {
        ImmutableList<object>? eventHandlers;
        lock (_handlers)
            if (!_handlers.TryGetValue(typeof(TEvent), out eventHandlers))
                return;

        foreach (UIEventHandler<TEvent> eventHandler in eventHandlers) {
            await eventHandler(@event, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task Publish<TEvent>(CancellationToken cancellationToken = default) where TEvent : class, new()
        => Publish(new TEvent(), cancellationToken);
}
