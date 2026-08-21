using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="ReloadUI"/> that recreates the WebView on reload.
/// </summary>
[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiReloadUI))]
public sealed class MauiReloadUI(IServiceProvider services) : ReloadUI(services)
{
    public override void Reload(bool clearLocalSettings = false)
    {
        Log.LogInformation("Reload requested");
        _ = DispatchToMainThread(async () => {
            Log.LogInformation("Reloading...");
            try {
                await Clear(clearLocalSettings).ConfigureAwait(true);
                MainPage.Current.RecreateWebView();
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed, terminating");
                Quit(); // We can't do much in this case
            }
        }, allowInline: false);
    }

    public override async Task ReplaceSession(CancellationToken cancellationToken)
    {
        // Nothing re-issues a MAUI session for us: it lives in the keychain, so it has to be minted,
        // switched to and stored before the WebView the reload recreates binds to it.
        await Services.GetRequiredService<MauiSession>().Replace(cancellationToken).ConfigureAwait(false);
        Reload(clearLocalSettings: true);
    }

    public override void Quit()
        => App.Current.Quit();
}
