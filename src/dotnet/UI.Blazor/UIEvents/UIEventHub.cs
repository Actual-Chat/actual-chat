using System.Diagnostics.CodeAnalysis;

namespace ActualChat.UI.Blazor;

public sealed class UIEventHub : IHasServices
{
    private readonly Dictionary<Type, ImmutableList<Delegate>> _handlers = new ();
    private Dispatcher? _dispatcher;

    private ILogger Log { get; }

    public IServiceProvider Services { get; }
    public Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();

    public UIEventHub(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        var configurators = services.GetRequiredService<IEnumerable<Action<UIEventHub>>>();
        foreach (var configurator in configurators)
            configurator.Invoke(this);
    }

    public void Subscribe<TEvent>(UIEventHandler<TEvent> handler)
        where TEvent: class, IUIEvent
    {
        lock (_handlers) {
            var eventHandlers = _handlers.GetValueOrDefault(typeof(TEvent));
            _handlers[typeof(TEvent)] = eventHandlers?.Add(handler) ?? ImmutableList.Create<Delegate>(handler);
        }
    }

    public void Unsubscribe<TEvent>(UIEventHandler<TEvent> handler)
        where TEvent: class, IUIEvent
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

    public Task Publish<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent: class, IUIEvent
    {
        ImmutableList<Delegate>? eventHandlers;
        lock (_handlers)
            if (!_handlers.TryGetValue(typeof(TEvent), out eventHandlers))
                return Task.CompletedTask;

        return Dispatcher.InvokeAsync(async () => {
            foreach (var eventHandler in eventHandlers) {
                try {
                    if (eventHandler is UIEventHandler<TEvent> h)
                        await h.Invoke(@event, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    Log.LogError(e, "UI event handler failed");
                }
            }
        });
    }

    public Task Publish<TEvent>(CancellationToken cancellationToken = default)
        where TEvent: class, IUIEvent, new()
        => Publish(new TEvent(), cancellationToken);
}
