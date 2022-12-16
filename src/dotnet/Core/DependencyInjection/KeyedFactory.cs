using Stl.Reflection;

namespace ActualChat.DependencyInjection;

public class KeyedFactory<TService, TKey> : IHasServices
    where TService : class
{
    public IServiceProvider Services { get; }
    public TService this[TKey key] => Factory.Invoke(Services, key);
    public Func<IServiceProvider, TKey, TService> Factory { get; init; }

    public KeyedFactory(IServiceProvider services, Func<IServiceProvider, TKey, TService>? factory = null)
    {
        Services = services;
        Factory = factory ?? DefaultFactory;
    }

    public KeyedFactory<TService, TKey> ToGeneric()
        => this;
    public KeyedFactory<TToService, TKey> ToGeneric<TToService>()
        where TToService : class
        => new CastingKeyedFactory<TToService,TKey,TService>(this);

    // Private methods

    protected static TService DefaultFactory(IServiceProvider services, TKey key)
        => (TService)typeof(TService).CreateInstance(services, key);
}

public sealed class KeyedFactory<TService, TKey, TImplementation> : KeyedFactory<TService, TKey>
    where TService : class
    where TImplementation : class, TService
{
    public KeyedFactory(IServiceProvider services, Func<IServiceProvider, TKey, TImplementation>? factory)
        : base(services, factory ?? DefaultFactory)
    { }

    // Private methods

    private static new TImplementation DefaultFactory(IServiceProvider services, TKey key)
        => (TImplementation)typeof(TImplementation).CreateInstance(services, key);
}
