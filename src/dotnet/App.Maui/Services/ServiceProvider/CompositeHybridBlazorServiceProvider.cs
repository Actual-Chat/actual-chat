namespace ActualChat.App.Maui;

public sealed class CompositeBlazorHybridServiceProvider : IServiceProvider, IAsyncDisposable, IDisposable
{
    private readonly CompositeBlazorHybridServiceProviderEngineScope _rootScope;
    private readonly MauiApp _mauiApp;

    public CompositeBlazorHybridServiceProvider(
        MauiApp mauiApp,
        Task<IServiceProvider> blazorServicesTask,
        Func<Type,bool> blazorServicesLookupSkipper)
    {
        _mauiApp = mauiApp;
        _rootScope = new CompositeBlazorHybridServiceProviderEngineScope(
            mauiApp.Services,
            new LazyServiceProviderEngineScope(blazorServicesTask, true),
            blazorServicesLookupSkipper,
            true);
    }

    public object? GetService(Type serviceType)
        => _rootScope.GetService(serviceType);

    public void Dispose()
    {
        _rootScope.Dispose();
        _mauiApp.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _rootScope.DisposeAsync().ConfigureAwait(false);
        _mauiApp.Dispose();
    }
}
