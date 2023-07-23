using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class App : Application, IHasServices
{
    public static new App Current => (App)Application.Current!;
    public static bool MustQuit { get; set; }

    private ILogger? _log;

    public IServiceProvider Services { get; }
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

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        Log.LogInformation("OnAppLinkRequestReceived: {Uri}", uri);
        if (!OrdinalIgnoreCaseEquals(uri.Host, MauiSettings.Host))
            return;

        var autoNavigationTasks = Services.GetRequiredService<AutoNavigationTasks>();
        autoNavigationTasks.Add(ForegroundTask.Run(async () => {
            var scopedServices = await ScopedServicesTask.ConfigureAwait(false);
            var url = new LocalUrl(uri.PathAndQuery + uri.Fragment);
            var autoNavigationUI = scopedServices.GetRequiredService<AutoNavigationUI>();
            await autoNavigationUI.DispatchNavigateTo(url, AutoNavigationReason.AppLink).ConfigureAwait(false);
        }, Log, "Failed to handle AppLink request"));
    }
}
