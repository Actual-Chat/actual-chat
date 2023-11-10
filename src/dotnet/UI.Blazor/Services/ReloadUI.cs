using ActualChat.Kvas;
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

    public virtual void Reload(bool clearCaches = false, bool clearLocalSettings = false)
    {
        Log.LogInformation("Reloading requested");
        var blazorCircuitContext = Services.GetRequiredService<AppBlazorCircuitContext>();
        _ = blazorCircuitContext.WhenReady.ContinueWith(_ => blazorCircuitContext.Dispatcher.InvokeAsync(async () => {
            Log.LogWarning("Reloading...");
            try {
                await Clear(clearCaches, clearLocalSettings).ConfigureAwait(true); // Nav requires UI context
                var nav = Services.GetRequiredService<NavigationManager>();
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
            var clientComputedCache = Services.GetService<IClientComputedCache>();
            if (clientComputedCache != null)
                await clientComputedCache.Clear(CancellationToken.None).ConfigureAwait(false);
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
