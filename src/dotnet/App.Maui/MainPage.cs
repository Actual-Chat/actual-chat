using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace ActualChat.App.Maui;

public class MainPage : ContentPage
{
    private static volatile MainPage _current = null!;

    public static MainPage Current => _current;

    public MainPage()
    {
        Interlocked.Exchange(ref _current, this);
        RecreateWebView();
        BackgroundColor = MauiSettings.SplashBackgroundColor;
        On<iOS>().SetUseSafeArea(true);
    }

    public void RecreateWebView()
    {
        var mauiWebView = new MauiWebView();
        Content = new Grid {
            new MauiSplash(),
            mauiWebView.BlazorWebView,
        };
    }

    public void Reload()
    {
        var mauiWebView = MauiWebView.Current;
        if (mauiWebView == null || mauiWebView.IsDead)
            RecreateWebView();
        else
            mauiWebView.HardNavigateTo(MauiWebView.BaseLocalUri.ToString());
    }
}
