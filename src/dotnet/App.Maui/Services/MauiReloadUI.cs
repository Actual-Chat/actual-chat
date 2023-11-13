using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public class MauiReloadUI : ReloadUI
{
    public MauiReloadUI(IServiceProvider services) : base(services) { }

    public override void Reload(bool clearCaches = false, bool clearLocalSettings = false)
    {
        Log.LogInformation("Reloading requested");
        _ = MainThread.InvokeOnMainThreadAsync(async () => {
            Log.LogWarning("Reloading...");
            try {
                await Clear(clearCaches, clearLocalSettings).ConfigureAwait(true);
                MainPage.Current.RecreateWebView(); // No MainPage.Current = no reload needed
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed, terminating");
                Quit(); // We can't do much in this case
            }
        });
    }

    public override void Quit()
        => App.Current.Quit();
}
