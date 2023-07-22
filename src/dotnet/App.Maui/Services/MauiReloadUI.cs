using ActualChat.UI.Blazor.Services;
using Stl.Fusion.Client.Caching;

namespace ActualChat.App.Maui.Services;

public class MauiReloadUI : ReloadUI
{
    public MauiReloadUI(IServiceProvider services) : base(services) { }

    public override void Reload(bool clearCache = false)
        => _ = MainThread.InvokeOnMainThreadAsync(async () => {
            if (clearCache) {
                Log.LogWarning("Cleaning cache...");
                try {
                    var cache = Services.GetService<IClientComputedCache>();
                    if (cache != null)
                        await cache.Clear().ConfigureAwait(true);
                }
                catch (Exception e) {
                    Log.LogError(e, "Cache clean-up failed");
                }
            }

            Log.LogWarning("Reloading...");
            try {
                DiscardScopedServices();
                MainPage.Current?.RecreateWebView(); // No MainPage.Current = no reload needed
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed, terminating");
                Application.Current!.Quit(); // We can't do much in this case
            }
        });
}
