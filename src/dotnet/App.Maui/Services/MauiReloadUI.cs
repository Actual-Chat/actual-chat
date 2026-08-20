using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="ReloadUI"/> that recreates the WebView on reload.
/// </summary>
[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiReloadUI))]
public class MauiReloadUI(IServiceProvider services) : ReloadUI(services)
{
    public override void Reload(bool clearCaches = false, bool clearLocalSettings = false)
    {
        Log.LogInformation("Reload requested");
        _ = DispatchToMainThread(async () => {
            Log.LogInformation("Reloading...");
            try {
                await Clear(clearCaches, clearLocalSettings).ConfigureAwait(true);
                // Recovers a session killed elsewhere - deactivated from another device, or expired.
                // Only the WebView is recreated here, so without this the app would keep using the
                // dead id until it restarts, and every sign-in would fail on it. The swap itself
                // leaves Session.Default-keyed computeds serving the old session until a restart,
                // which is why sign-out no longer deactivates - see MauiAccountUI.
                await Services.GetRequiredService<MauiSession>().Revalidate().ConfigureAwait(true);
                MainPage.Current.RecreateWebView();
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed, terminating");
                Quit(); // We can't do much in this case
            }
        }, allowInline: false);
    }

    public override void Quit()
        => App.Current.Quit();
}
