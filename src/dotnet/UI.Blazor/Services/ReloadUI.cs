using Stl.Fusion.Client.Caching;

namespace ActualChat.UI.Blazor.Services;

public class ReloadUI
{
    private IServiceProvider Services { get; }
    private ILogger Log { get; }

    public ReloadUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
    }

    public void Reload(bool clearCache, string? url = null)
    {
        var blazorCircuitContext = Services.GetRequiredService<AppBlazorCircuitContext>();
        _ = blazorCircuitContext.WhenReady.ContinueWith(_ => blazorCircuitContext.Dispatcher.InvokeAsync(async () => {
            Log.LogWarning("Reloading URL: {Url}", url);
            try {
                if (clearCache) {
                    var cache = Services.GetService<IClientComputedCache>();
                    if (cache != null)
                        await cache.Clear().ConfigureAwait(true);
                }
                var nav = Services.GetRequiredService<NavigationManager>();
                nav.NavigateTo(url ?? Links.Home, true);
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed");
            }
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
