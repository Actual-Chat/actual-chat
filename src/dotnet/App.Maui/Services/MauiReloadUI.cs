using ActualChat.UI.Blazor.Services;
using Stl.Fusion.Client.Caching;

namespace ActualChat.App.Maui.Services;

public class MauiReloadUI : ReloadUI
{
    private readonly object _lock = new();
    private Task? _reloadTask;
    private Task? _clearCacheTask;

    public MauiReloadUI(IServiceProvider services) : base(services) { }

    public override void Reload(bool clearCache)
    {
        lock (_lock) {
            if (clearCache)
                _clearCacheTask ??= Task.Run(async () => {
                    Log.LogWarning("Clearing cache...");
                    try {
                        var cache = Services.GetService<IClientComputedCache>();
                        if (cache != null)
                            await cache.Clear().ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Log.LogError(e, "Cache clean-up failed");
                    }
                });
            _reloadTask ??= MainThread.InvokeOnMainThreadAsync(async () => {
                Task? clearCacheTask;
                lock (_lock)
                    clearCacheTask = _clearCacheTask;
                if (clearCacheTask != null)
                    await clearCacheTask.ConfigureAwait(true);

                Log.LogWarning("Restarting...");
                while (true) {
                    try {
                        Application.Current?.Quit();
                    }
                    catch (Exception e) {
                        Log.LogError(e, "Restart failed, retrying...");
                    }
                    await Task.Delay(TimeSpan.FromSeconds(0.5)).ConfigureAwait(true);
                }
            });
        }
    }
}
