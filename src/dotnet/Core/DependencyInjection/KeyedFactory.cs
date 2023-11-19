using System.Diagnostics.CodeAnalysis;

namespace ActualChat.DependencyInjection;

public class KeyedFactory<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey>(
    IServiceProvider services,
    Func<IServiceProvider, TKey, TService>? factory = null
    ) : IHasServices
    where TService : class
{
    public IServiceProvider Services { get; } = services;
    public TService this[TKey key] => Factory.Invoke(Services, key);
    public Func<IServiceProvider, TKey, TService> Factory { get; init; } = factory ?? DefaultFactory;

    public KeyedFactory<TService, TKey> ToGeneric()
        => this;
    public KeyedFactory<TToService, TKey> ToGeneric<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TToService>()
        where TToService : class
        => new CastingKeyedFactory<TToService,TKey,TService>(this);

    // Private methods

    protected static TService DefaultFactory(IServiceProvider services, TKey key)
        => (TService)typeof(TService).CreateInstance(services, key);
}

public sealed class KeyedFactory<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>(
    IServiceProvider services,
    Func<IServiceProvider, TKey, TImplementation>? factory
    ) : KeyedFactory<TService, TKey>(services, factory ?? DefaultFactory)
    where TService : class
    where TImplementation : class, TService
{
    // Private methods

    private static new TImplementation DefaultFactory(IServiceProvider services, TKey key)
        => (TImplementation)typeof(TImplementation).CreateInstance(services, key);
}
