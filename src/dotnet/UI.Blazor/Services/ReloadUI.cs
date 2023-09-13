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

    public virtual void Reload(bool clearCaches = false)
    {
        Log.LogInformation("Reloading requested");
        var blazorCircuitContext = Services.GetRequiredService<AppBlazorCircuitContext>();
        _ = blazorCircuitContext.WhenReady.ContinueWith(_ => blazorCircuitContext.Dispatcher.InvokeAsync(async () => {
            Log.LogWarning("Reloading...");
            try {
                if (clearCaches)
                    await ClearCaches();
                var nav = Services.GetRequiredService<NavigationManager>();
                nav.NavigateTo(Links.Home, true);
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed");
            }
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public virtual async Task ClearCaches()
    {
        Log.LogWarning("Cleaning caches & local settings...");
        try {
            var localSettings = Services.GetRequiredService<LocalSettings>();
            var clientComputedCache = Services.GetService<IClientComputedCache>();
            var clearLocalSettingsTask = localSettings.Clear();
            if (clientComputedCache != null)
                await clientComputedCache.Clear(CancellationToken.None).ConfigureAwait(false);
            await clearLocalSettingsTask.ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "ClearCaches failed");
        }
    }

    public virtual void Quit()
        => throw new NotSupportedException("Can't close web app.");
}
