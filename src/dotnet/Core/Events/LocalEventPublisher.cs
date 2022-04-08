namespace ActualChat.Events;

public class LocalEventPublisher: IEventPublisher
{
    private readonly IServiceProvider _serviceProvider;

    public LocalEventPublisher(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public Task Publish<T>(T @event, CancellationToken cancellationToken) where T : class, IEvent
    {
        var registeredHub = _serviceProvider.GetRequiredService<LocalEventHub<T>>();
        return registeredHub.Publisher.Publish(@event, cancellationToken);
    }
}
