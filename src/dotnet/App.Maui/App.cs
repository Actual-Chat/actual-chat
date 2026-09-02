using ActualChat.UI.Blazor.Services;
using Application = Microsoft.Maui.Controls.Application;

namespace ActualChat.App.Maui;

public class App(IServiceProvider services) : Application
{
    public static new App Current => (App)Application.Current!;
    public static bool MustMinimizeOnQuit { get; private set; } = true;

    private IServiceProvider Services { get; } = services;
    private ILogger Log => field ??= Services.LogFor(GetType());

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage());
        window.Destroying += (_, _) => FlushSentryData();
        window.Title =
            MauiSettings.UseLocalhost
                ? $"{CoreConstants.AppName} (Local)"
                : MauiSettings.IsDevApp
                    ? $"{CoreConstants.AppName} (Dev)"
                    : CoreConstants.AppName;
        return window;
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        if (!string.Equals(MauiSettings.Host, MauiSettings.DefaultHost, StringComparison.OrdinalIgnoreCase)) {
            // TODO(DF): Think if it's possible to handle this in host override mode.
            Log.LogWarning("OnAppLinkRequestReceived: {Uri} -> ignore (host override mode is on)", uri);
            return;
        }
        // The Android intent filter advertises http alongside https, so the link is reduced to its
        // local part here - BaseUri is always https and would otherwise reject an http link.
        if (!LocalUrl.TryParseAppLink(uri.ToString(), MauiSettings.BaseUri, out var localUrl)) {
            Log.LogWarning("OnAppLinkRequestReceived: {Uri} -> ignore (wrong origin)", uri);
            return;
        }

        AppNavigationQueue.EnqueueOrNavigateToUrl(localUrl.Value, AutoNavigationReason.AppLink);
    }

    public new void Quit()
    {
        MustMinimizeOnQuit = false;
        base.Quit();
    }

    private static void FlushSentryData()
    {
        var tracer = Tracer.Default[nameof(App)];
        using (tracer.MethodRegion()) {
            MauiDiagnostics.TracerProvider?.DisposeSilently();
            if (SentrySdk.IsEnabled)
                SentrySdk.Flush();
        }
    }
}
