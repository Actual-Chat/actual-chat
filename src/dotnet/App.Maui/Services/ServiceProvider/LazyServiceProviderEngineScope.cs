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
    private IServiceProvider? _resolvedServices;

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
        var serviceProvider = GetServiceProviderInternal();
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
            serviceProvider = _resolvedServices;
        }
        (serviceProvider as IDisposable)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        IServiceProvider? services;
        lock (_lock) {
            if (_isDisposed)
                return;
            _isDisposed = true;
            services = _resolvedServices;
        }
        if (services == null)
            return;
        await DisposeServicesHelper.DisposeAsync(services).ConfigureAwait(false);
    }

    private IServiceProvider GetServiceProviderInternal()
    {
        lock (_lock) {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(IServiceProvider));
            if (_resolvedServices != null)
                return _resolvedServices;

            // Block here on purpose until we get the result
 #pragma warning disable VSTHRD002
            var serviceProvider = _serviceProviderTask.GetAwaiter().GetResult();
 #pragma warning restore VSTHRD002
            _resolvedServices = serviceProvider;
            return _resolvedServices;
        }
    }

    private async Task<IServiceProvider> CreateScopedServiceProvider()
    {
        var services = await _serviceProviderTask.ConfigureAwait(false);
        return services.CreateScope().ServiceProvider;
    }
}
