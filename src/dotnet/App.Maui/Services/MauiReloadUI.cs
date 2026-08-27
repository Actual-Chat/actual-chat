using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// MAUI implementation of <see cref="ReloadUI"/> that recreates the WebView on reload.
/// </summary>
[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiReloadUI))]
public sealed class MauiReloadUI(IServiceProvider services) : ReloadUI(services)
{
    public override async Task<bool> ReplaceSession(Session? invalidSession, CancellationToken cancellationToken)
    {
        // Nothing re-issues a MAUI session for us: it lives in the keychain, so it has to be minted,
        // switched to and stored before the WebView the reload recreates binds to it.
        var isReplaced = await Services.GetRequiredService<MauiSession>()
            .Replace(invalidSession, cancellationToken)
            .ConfigureAwait(false);
        if (isReplaced)
            Reload(clearLocalSettings: true);

        return isReplaced;
    }

    public override void Quit()
        => App.Current.Quit();

    // Protected methods

    protected override Task Dispatch(Func<Task> action)
        => DispatchToMainThread(async () => {
            // Reloads queue on the main thread, so another scope's reload can replace this WebView -
            // and dispose this scope - before ours runs. Resolving anything from a disposed scope
            // throws, and OnReloadFailed turns that into Quit(), so let the live scope reload.
            if (!ReferenceEquals(MauiWebView.Current?.ScopedServices, Services))
                return;

            await action().ConfigureAwait(true);
        }, allowInline: false);

    protected override Task ForceReload()
    {
        MainPage.Current.RecreateWebView();
        return Task.CompletedTask;
    }

    protected override void OnReloadFailed(Exception error)
    {
        if (error is ObjectDisposedException) {
            // This scope went away mid-reload, so another one's reload already replaced its WebView
            Log.LogWarning(error, "Reload abandoned: its scope is gone");
            return;
        }

        Log.LogError(error, "Reload failed, terminating");
        Quit(); // We can't do much in this case
    }
}
