namespace ActualChat.App.Maui;

public sealed class LazyServiceProviderEngineScope :
    IServiceScope,
    IServiceProvider,
    IServiceScopeFactory,
    IAsyncDisposable,
    IDisposable
{
    private Task<IServiceProvider> _serviceProviderTask;
    private readonly bool _isRootScope;
    private bool _isDisposed;
    private readonly object _lock = new ();
    private IServiceProvider? _resolvedServiceProvider;

    public IServiceProvider ServiceProvider => this;

    public LazyServiceProviderEngineScope(Task<IServiceProvider> serviceProviderTask, bool isRootScope)
    {
        _serviceProviderTask = serviceProviderTask;
        _isRootScope = isRootScope;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceScopeFactory))
            return this;
        if (serviceType == typeof(IServiceProvider))
            return this;
        var serviceProvider = GetServiceProviderInternal(serviceType);
        var result = serviceProvider.GetService(serviceType);
        return result;
    }

    public void SetupOnResolved(Action<IServiceProvider> setup)
        => _serviceProviderTask = _serviceProviderTask
            .ContinueWith(t => {
                var services = t.Result;
                setup(services);
                return services;
            }, TaskScheduler.Default);

    public IServiceScope CreateScope()
        => new LazyServiceProviderEngineScope(
            CreateScopedServiceProvider(),
            false);

    public void Dispose()
    {
        IServiceProvider? serviceProvider;
        lock (_lock) {
            if (_isDisposed)
                return;
            _isDisposed = true;
            serviceProvider = _resolvedServiceProvider;
        }
        (serviceProvider as IDisposable)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        IServiceProvider? serviceProvider;
        lock (_lock) {
            if (_isDisposed)
                return;
            _isDisposed = true;
            serviceProvider = _resolvedServiceProvider;
        }
        if (serviceProvider is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    private IServiceProvider GetServiceProviderInternal(Type serviceType)
    {
        lock (_lock) {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(IServiceProvider));
            if (_resolvedServiceProvider != null)
                return _resolvedServiceProvider;
            if (!_serviceProviderTask.IsCompleted)
                Tracer.Default.Point($"About to await service provider building. Requested service '{serviceType}'"  + Environment.NewLine + Environment.StackTrace);
            var serviceProvider = _serviceProviderTask.GetAwaiter().GetResult();
            Tracer.Default.Point($"LazyServiceProviderEngineScope.ServiceProvider Resolved. Requested service '{serviceType}'" + Environment.NewLine + Environment.StackTrace);
            _resolvedServiceProvider = serviceProvider;
            return _resolvedServiceProvider;
        }
    }

    private async Task<IServiceProvider> CreateScopedServiceProvider()
    {
        var services = await _serviceProviderTask.ConfigureAwait(false);
        return services.CreateScope().ServiceProvider;
    }
}
