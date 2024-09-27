using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class App : Application
{
    public static new App Current => (App)Application.Current!;
    public static bool MustMinimizeOnQuit { get; private set; } = true;

    private ILogger? _log;

    private IServiceProvider Services { get; }
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public App(MainPage mainPage, IServiceProvider services)
    {
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.SetWindowSoftInputModeAdjust(
            this,
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
#if WINDOWS
        // Allows to load mixed content into WebView on Windows
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
            "--disable-features=AutoupgradeMixedContent");
#endif
        Services = services;
        MainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
        window.Destroying += (_, _) => FlushSentryData();
        window.Title = MauiSettings.IsDevApp ? "Actual Chat (Dev)" : "Actual Chat";
        return window;
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        if (!OrdinalIgnoreCaseEquals(MauiSettings.Host, MauiSettings.DefaultHost)) {
            // TODO(DF): Think if it's possible to handle this in host override mode.
            Log.LogWarning("OnAppLinkRequestReceived: {Uri} -> ignore (host override mode is on)", uri);
            return;
        }
        if (!OrdinalIgnoreCaseEquals(uri.Host, MauiSettings.Host)) {
            Log.LogWarning("OnAppLinkRequestReceived: {Uri} -> ignore (wrong host)", uri);
            return;
        }

        AppNavigationQueue.EnqueueOrNavigateToNotificationUrl(uri.ToString());
    }

    public new void Quit()
    {
        MustMinimizeOnQuit = false;
        base.Quit();
    }

    private static void FlushSentryData()
    {
        using (MauiDiagnostics.Tracer.Region()) {
            MauiDiagnostics.TracerProvider?.DisposeSilently();
            if (SentrySdk.IsEnabled)
                SentrySdk.Flush();
        }
    }
}
