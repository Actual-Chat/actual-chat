namespace ActualChat.App.Maui;

public sealed class CompositeMauiBlazorServiceProviderEngineScope :
    IServiceScope,
    IServiceProvider,
    IServiceScopeFactory,
    IAsyncDisposable
{
    private readonly IServiceProvider _mauiAppServices;
    private readonly LazyServiceProviderEngineScope _blazorServices;
    private readonly Func<Type, bool> _blazorServiceFilter;
    private readonly bool _isRootScope;

    public IServiceProvider ServiceProvider => this;

    public CompositeMauiBlazorServiceProviderEngineScope(
        IServiceProvider mauiAppServices,
        LazyServiceProviderEngineScope blazorServices,
        Func<Type, bool> blazorServiceFilter,
        bool isRootScope)
    {
        _mauiAppServices = mauiAppServices;
        _blazorServices = blazorServices;
        _blazorServiceFilter = blazorServiceFilter;
        _blazorServices.SetupOnResolved(c => {
            var serviceResolver = c.GetService<DelegateServiceResolver>();
            serviceResolver?.SetResolver(serviceType => _mauiAppServices.GetService(serviceType));
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
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            var itemType = serviceType.GetGenericArguments()[0];
            var typedResult = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
            var enumerableResult = (IEnumerable)result!;
            var blazorEnumerableResult = (IEnumerable)_blazorServices.GetService(serviceType)!;
            foreach (var item in enumerableResult)
                typedResult.Add(item);
            foreach (var item in blazorEnumerableResult)
                typedResult.Add(item);
            return typedResult;
        }
        if (result != null)
            return result;

        return _blazorServiceFilter.Invoke(serviceType) ? _blazorServices.GetService(serviceType) : null;
    }

    public IServiceScope CreateScope()
        => new CompositeMauiBlazorServiceProviderEngineScope(
            _mauiAppServices.CreateScope().ServiceProvider,
            (LazyServiceProviderEngineScope)_blazorServices.CreateScope().ServiceProvider,
            _blazorServiceFilter,
            false);

    public void Dispose()
    {
        _blazorServices.Dispose();
        (_mauiAppServices as IDisposable)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _blazorServices.SafelyDisposeAsync().ConfigureAwait(false);
        await _mauiAppServices.SafelyDisposeAsync().ConfigureAwait(false);
    }
}
