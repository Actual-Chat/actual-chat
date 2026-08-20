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
                // Sign-out deactivates the session - mint a replacement before the new WebView binds it.
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
