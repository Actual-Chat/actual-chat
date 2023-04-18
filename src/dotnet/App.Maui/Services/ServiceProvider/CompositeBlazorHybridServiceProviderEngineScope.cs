namespace ActualChat.App.Maui;

public sealed class CompositeBlazorHybridServiceProviderEngineScope :
    IServiceScope,
    IServiceProvider,
    IServiceScopeFactory,
    IAsyncDisposable,
    IDisposable
{
    private readonly IServiceProvider _mauiAppServices;
    private readonly LazyServiceProviderEngineScope _blazorServices;
    private readonly Func<Type, bool> _blazorServicesLookupFilter;
    private readonly bool _isRootScope;

    public IServiceProvider ServiceProvider => this;

    public CompositeBlazorHybridServiceProviderEngineScope(
        IServiceProvider mauiAppServices,
        LazyServiceProviderEngineScope blazorServices,
        Func<Type, bool> blazorServicesLookupFilter,
        bool isRootScope)
    {
        _mauiAppServices = mauiAppServices;
        _blazorServices = blazorServices;
        _blazorServicesLookupFilter = blazorServicesLookupFilter;
        _blazorServices.SetupOnResolved(c => {
            var delegatedServiceResolver = c.GetService<DelegateServiceResolver>();
            delegatedServiceResolver?.SetResolver(
                serviceType => _mauiAppServices.GetService(serviceType));
        });
        _isRootScope = isRootScope;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceScopeFactory))
            return this;
        if (serviceType == typeof(IServiceProvider))
            return this;
        var result = _mauiAppServices.GetService(serviceType);
        if (result != null)
            return result;
        if (_blazorServicesLookupFilter(serviceType))
            return null;
        return _blazorServices.GetService(serviceType);
    }

    public IServiceScope CreateScope()
        => new CompositeBlazorHybridServiceProviderEngineScope(
            _mauiAppServices.CreateScope().ServiceProvider,
            (LazyServiceProviderEngineScope)_blazorServices.CreateScope().ServiceProvider,
            _blazorServicesLookupFilter,
            false);

    public void Dispose()
    {
        _blazorServices.Dispose();
        (_mauiAppServices as IDisposable)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _blazorServices.DisposeAsync().ConfigureAwait(false);
        await DisposeServicesHelper.DisposeAsync(_mauiAppServices).ConfigureAwait(false);
    }
}
