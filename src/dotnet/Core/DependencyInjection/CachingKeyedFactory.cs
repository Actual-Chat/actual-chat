using System.Diagnostics.CodeAnalysis;

namespace ActualChat.DependencyInjection;

public class CachingKeyedFactory<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey>
    : KeyedFactory<TService, TKey>
    where TService : class
    where TKey : notnull
{
    private IThreadSafeLruCache<TKey, TService> Cache { get; }

    public CachingKeyedFactory(
        IServiceProvider services,
        int capacity,
        bool useConcurrentCache = false,
        Func<IServiceProvider, TKey, TService>? factory = null)
        : base(services, factory)
    {
        Cache = useConcurrentCache
            ? new ConcurrentLruCache<TKey, TService>(capacity)
            : new ThreadSafeLruCache<TKey, TService>(capacity);
        Factory = CachingFactory(factory ?? DefaultFactory);
    }

    public CachingKeyedFactory(
        IServiceProvider services,
        IThreadSafeLruCache<TKey, TService> cache,
        Func<IServiceProvider, TKey, TService>? factory = null)
        : base(services, factory)
    {
        Cache = cache;
        Factory = CachingFactory(factory ?? DefaultFactory);
    }

    protected Func<IServiceProvider, TKey, TService> CachingFactory(Func<IServiceProvider, TKey, TService> factory)
        => (c, key) => {
            if (Cache.TryGetValue(key, out var service))
                return service;

            service = factory.Invoke(c, key);
            if (Cache.TryAdd(key, service))
                return service;

            // TryAdd failed, so we can try to pull the cached value once more
            return Cache.TryGetValue(key, out var existingService) ? existingService : service;
        };
}

public class CachingKeyedFactory<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>
    : CachingKeyedFactory<TService, TKey>
    where TService : class
    where TKey : notnull
    where TImplementation : class, TService
{
    public CachingKeyedFactory(
        IServiceProvider services,
        int capacity,
        bool useConcurrentCache = false,
        Func<IServiceProvider, TKey, TService>? factory = null
        ) : base(services, capacity, useConcurrentCache, factory)
        => Factory = CachingFactory(factory ?? DefaultFactory);

    public CachingKeyedFactory(
        IServiceProvider services,
        IThreadSafeLruCache<TKey, TService> cache,
        Func<IServiceProvider, TKey, TService>? factory = null
        ) : base(services, cache, factory)
        => Factory = CachingFactory(factory ?? DefaultFactory);

    // Private methods

    private static new TImplementation DefaultFactory(IServiceProvider services, TKey key)
        => (TImplementation)typeof(TImplementation).CreateInstance(services, key);
}
