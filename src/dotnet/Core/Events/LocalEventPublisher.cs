namespace ActualChat.Events;

public class LocalEventPublisher: IEventPublisher
{
    private readonly IServiceProvider _services;

    public LocalEventPublisher(IServiceProvider services)
        => _services = services;

    public Task Publish<T>(T @event, CancellationToken cancellationToken) where T : class, IEvent
    {
        var registeredHub = _services.GetRequiredService<LocalEventHub<T>>();
        return registeredHub.Publisher.Publish(@event, cancellationToken);
    }
}
