namespace ActualChat.DependencyInjection;

public sealed class ServiceFactory<TService, TKey> : IHasServices
{
    public IServiceProvider Services { get; }
    public Func<IServiceProvider, TKey, TService> Factory { get; init; }
    public TService this[TKey key] => Factory.Invoke(Services, key);

    public ServiceFactory(IServiceProvider services, Func<IServiceProvider, TKey, TService> factory)
    {
        Services = services;
        Factory = factory;
    }
}
