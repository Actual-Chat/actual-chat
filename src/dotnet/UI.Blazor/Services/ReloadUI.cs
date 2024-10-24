using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualLab.Fusion.Client.Caching;

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

    public virtual void Reload(bool clearCaches = false, bool clearLocalSettings = false)
    {
        Log.LogInformation("Reload requested");
        var circuitContext = Services.GetRequiredService<AppBlazorCircuitContext>();
        _ = circuitContext.WhenReady.ContinueWith(_ => circuitContext.Dispatcher.InvokeAsync(async () => {
            Log.LogInformation("Reloading...");
            try {
                var hostInfo = Services.HostInfo();
                var nav = Services.GetRequiredService<NavigationManager>();
                await Clear(clearCaches, clearLocalSettings).ConfigureAwait(true); // Nav requires UI context
                if (hostInfo.HostKind.IsApp())
                    AppNavigationQueue.Reset();
                nav.NavigateTo(Links.Home, true);
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed");
            }
        }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Task Clear(bool clearCaches, bool clearLocalSettings)
    {
        if (!(clearCaches || clearLocalSettings))
            return Task.CompletedTask;

        var clearTasks = new List<Task>();
        if (clearCaches)
            clearTasks.Add(ClearCaches());
        if (clearLocalSettings)
            clearTasks.Add(ClearLocalSettings());
        return Task.WhenAll(clearTasks);
    }

    public virtual async Task ClearCaches()
    {
        Log.LogWarning("Cleaning caches...");
        try {
            var remoteComputedCache = Services.GetService<IRemoteComputedCache>();
            if (remoteComputedCache != null)
                await remoteComputedCache.Clear(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "ClearCaches failed");
        }
    }

    public virtual async Task ClearLocalSettings()
    {
        Log.LogWarning("Cleaning local settings...");
        try {
            var localSettings = Services.GetRequiredService<LocalSettings>();
            await localSettings.Clear().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "ClearLocalSettings failed");
        }
    }

    public virtual void Quit()
        => throw new NotSupportedException("Can't close web app.");
}
