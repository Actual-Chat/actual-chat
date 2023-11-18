namespace ActualChat.DependencyInjection;

public sealed class CompositeServiceProvider :
    IServiceScope,
    IServiceProvider,
    IServiceScopeFactory,
    IAsyncDisposable
{
    private readonly IServiceProvider _nonLazyServices;
    private readonly LazyServiceProvider _lazyServices;
    private readonly object? _disposeOnDispose;

    public IServiceProvider ServiceProvider => this;

    public CompositeServiceProvider(
        IServiceProvider nonLazyServices,
        Task<IServiceProvider> lazyServicesTask,
        Func<Type, bool>? lazyServiceFilter = null,
        object? disposeOnDispose = null)
    {
        _nonLazyServices = nonLazyServices;
        _lazyServices = new LazyServiceProvider(lazyServicesTask, lazyServiceFilter, OnLazyServicesReady);
        _disposeOnDispose = disposeOnDispose ?? nonLazyServices;
    }

    public void Dispose()
    {
        _lazyServices.Dispose();
        if (_disposeOnDispose is IDisposable d)
            d.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _lazyServices.DisposeAsync().ConfigureAwait(false);
        if (_disposeOnDispose is IAsyncDisposable ad)
            await ad.DisposeAsync().ConfigureAwait(false);
        else if (_disposeOnDispose is IDisposable d)
            d.Dispose();
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceScopeFactory))
            return this;
        if (serviceType == typeof(IServiceProvider))
            return this;

        var result = _nonLazyServices.GetService(serviceType);
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            var itemType = serviceType.GetGenericArguments()[0];
            var services = (IList)typeof(List<>).MakeGenericType(itemType).CreateInstance();
            var nonLazyServices = (IEnumerable?)result;
            var lazyServices = (IEnumerable?)_lazyServices.GetService(serviceType);
            if (nonLazyServices != null)
                foreach (var item in nonLazyServices)
                    services.Add(item);
            if (lazyServices != null)
                foreach (var item in lazyServices)
                    services.Add(item);
            return services;
        }
        return result ?? _lazyServices.GetService(serviceType);
    }

    public IServiceScope CreateScope()
    {
 #pragma warning disable CA2000
        var nonLazyServices = _nonLazyServices.CreateScope().ServiceProvider;
 #pragma warning restore CA2000
        var whenLazyServicesReady = _lazyServices.CreateScopedProvider();
        return new CompositeServiceProvider(nonLazyServices, whenLazyServicesReady, _lazyServices.LazyServiceFilter);
    }

    // Private methods

    private void OnLazyServicesReady(IServiceProvider lazyServices)
    {
        var nonLazyServiceAccessor = lazyServices.GetService<NonLazyServiceAccessor>();
        if (nonLazyServiceAccessor != null)
            nonLazyServiceAccessor.NonLazyServices = _nonLazyServices;
    }
}
