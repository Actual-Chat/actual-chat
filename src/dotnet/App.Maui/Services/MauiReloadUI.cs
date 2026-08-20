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
                // Our own sign-out deactivates the session; it can also be killed elsewhere or expire.
                // Revalidate mints a fresh one and re-binds the RPC connection, so the WebView recreated
                // next binds every Session.Default-keyed computed to the new session, not the dead id.
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
