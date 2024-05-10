namespace ActualChat.DependencyInjection;

public sealed class LazyServiceProvider(
    Task<IServiceProvider> whenLazyServicesReady,
    Func<Type, bool>? lazyServiceFilter,
    Action<IServiceProvider>? onLazyServicesReady
    ) : IServiceScope, IServiceProvider, IServiceScopeFactory, IAsyncDisposable
{
    private readonly object _lock = new ();
    private volatile IServiceProvider? _lazyServices;
    private bool _isDisposed;

    public Task<IServiceProvider> WhenLazyServicesReady = whenLazyServicesReady;
    public readonly Func<Type, bool>? LazyServiceFilter = lazyServiceFilter;
    public readonly Action<IServiceProvider>? OnLazyServicesReady = onLazyServicesReady;

    public IServiceProvider ServiceProvider => this;

    public void Dispose()
    {
        IServiceProvider? lazyServices;
        // Double-check locking
        // ReSharper disable once InconsistentlySynchronizedField
        lock (_lock) {
            if (_isDisposed) return;

            _isDisposed = true;
            lazyServices = _lazyServices;
            _lazyServices = null;
            WhenLazyServicesReady = Task.FromException<IServiceProvider>(new ObjectDisposedException(nameof(IServiceProvider)));
        }
        (lazyServices as IDisposable)?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        IServiceProvider? lazyServices;
        // Double-check locking
        // ReSharper disable once InconsistentlySynchronizedField
        if (_isDisposed) return default;
        lock (_lock) {
            if (_isDisposed) return default;

            _isDisposed = true;
            lazyServices = _lazyServices;
            _lazyServices = null;
            WhenLazyServicesReady = Task.FromException<IServiceProvider>(new ObjectDisposedException(nameof(IServiceProvider)));
        }
        return DisposableExt.DisposeUnknownSilently(lazyServices);
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceScopeFactory))
            return this;
        if (serviceType == typeof(IServiceProvider))
            return this;

        if (LazyServiceFilter != null && !LazyServiceFilter.Invoke(serviceType))
            return null;

        return GetLazyServices(serviceType).GetService(serviceType);
    }

    public IServiceScope CreateScope()
        => new LazyServiceProvider(CreateScopedProvider(), LazyServiceFilter, OnLazyServicesReady);

    public async Task<IServiceProvider> CreateScopedProvider()
    {
        await WhenLazyServicesReady.ConfigureAwait(false);
        // It's important to call GetLazyServices here,
        // otherwise OnLazyServicesReady won't be invoked,
        // and root service provider won't be augmented
#pragma warning disable CA2000
        return GetLazyServices(null).CreateScope().ServiceProvider;
#pragma warning restore CA2000
    }

    // Private methods

    private IServiceProvider GetLazyServices(Type? requestedType)
    {
        // Double-check locking
        // ReSharper disable once InconsistentlySynchronizedField
        if (_lazyServices != null)
            return _lazyServices;

        lock (_lock) {
            ObjectDisposedException.ThrowIf(_isDisposed, typeof(IServiceProvider));
            if (_lazyServices != null)
                return _lazyServices;

            if (requestedType != null)
                StaticLog.For(GetType()).LogInformation(
                    nameof(LazyServiceProvider) + ": becoming non-lazy to resolve {RequestedType}",
                    requestedType);

#pragma warning disable VSTHRD002
            // Block here on purpose until we get the result
            var lazyServices = WhenLazyServicesReady.Result;
#pragma warning restore VSTHRD002
            OnLazyServicesReady?.Invoke(lazyServices);
            return _lazyServices = lazyServices;
        }
    }
}
