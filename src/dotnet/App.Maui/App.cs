using ActualChat.UI.Blazor.Services;
using Sentry;

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
#if IS_DEV_MAUI
        window.Title = "Actual Chat (Dev)";
#else
        window.Title = "Actual Chat";
#endif
        return window;
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        Log.LogInformation("OnAppLinkRequestReceived: {Uri}", uri);
        if (!OrdinalIgnoreCaseEquals(uri.Host, MauiSettings.Host))
            return;

        var url = new LocalUrl(uri.PathAndQuery + uri.Fragment);
        var autoNavigationTasks = Services.GetRequiredService<AutoNavigationTasks>();
        autoNavigationTasks.Add(DispatchToBlazor(
            c => c.GetRequiredService<AutoNavigationUI>().DispatchNavigateTo(url, AutoNavigationReason.AppLink),
            $"AutoNavigationUI.DispatchNavigateTo(\"{url}\", AppLink)"));
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
