namespace ActualChat.App.Maui;

public class App : Microsoft.Maui.Controls.Application
{
    private ILogger Log { get; }

    public App(MainPage mainPage, ILogger<App> log)
    {
        Log = log;
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application
            .SetWindowSoftInputModeAdjust(this, Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
        MainPage = mainPage;
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        Log.LogDebug("OnAppLinkRequestReceived: '{Uri}'", uri);
        Services.AppLinks.OnAppLinkRequestReceived(uri);
    }
}
