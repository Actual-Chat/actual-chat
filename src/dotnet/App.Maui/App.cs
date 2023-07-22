namespace ActualChat.App.Maui;

public class App : Application
{
    private ILogger Log { get; }

    public App(MainPage mainPage, ILogger<App> log)
    {
        Log = log;
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.SetWindowSoftInputModeAdjust(
            this,
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
#if WINDOWS
        // Allows to load mixed content into WebView on Windows
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
            "--disable-features=AutoupgradeMixedContent");
#endif

        MainPage = mainPage;
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        Log.LogDebug("OnAppLinkRequestReceived: '{Uri}'", uri);
        Services.AppLinks.OnAppLinkRequestReceived(uri);
    }
}
