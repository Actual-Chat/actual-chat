namespace ActualChat.App.Maui;

public sealed class CompositeMauiBlazorServiceProvider : IServiceProvider, IAsyncDisposable, IDisposable
{
    private readonly CompositeMauiBlazorServiceProviderEngineScope _rootScope;
    private readonly MauiApp _mauiApp;

    public CompositeMauiBlazorServiceProvider(
        MauiApp mauiApp,
        Task<IServiceProvider> blazorServicesTask,
        Func<Type, bool> blazorServiceFilter)
    {
        _mauiApp = mauiApp;
        _rootScope = new CompositeMauiBlazorServiceProviderEngineScope(
            mauiApp.Services,
            new LazyServiceProviderEngineScope(blazorServicesTask, true),
            blazorServiceFilter,
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
