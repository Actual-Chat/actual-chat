using Stl.Fusion.Client.Caching;

namespace ActualChat.UI.Blazor.Services;

public class ReloadUI
{
    protected IServiceProvider Services { get; }
    protected ILogger Log { get; }

    public ReloadUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
    }

    public virtual void Reload(bool clearCache = false)
    {
        var blazorCircuitContext = Services.GetRequiredService<AppBlazorCircuitContext>();
        _ = blazorCircuitContext.WhenReady.ContinueWith(_ => blazorCircuitContext.Dispatcher.InvokeAsync(async () => {
            Log.LogWarning("Reloading...");
            try {
                if (clearCache) {
                    var cache = Services.GetService<IClientComputedCache>();
                    if (cache != null)
                        await cache.Clear().ConfigureAwait(true);
                }
                var nav = Services.GetRequiredService<NavigationManager>();
                nav.NavigateTo(Links.Home, true);
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed");
            }
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public virtual void Quit()
        => throw new NotSupportedException("Can't close web app.");
}
