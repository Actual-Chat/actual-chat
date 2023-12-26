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
        On<iOS>().SetUseSafeArea(true);
        BackgroundColor = MauiSettings.SplashBackgroundColor;
        RecreateWebView();
    }

    public void RecreateWebView()
        => Content = new MauiWebView().BlazorWebView;

    public void Reload()
    {
        var mauiWebView = MauiWebView.Current;
        if (mauiWebView == null || mauiWebView.IsDead)
            RecreateWebView();
        else
            mauiWebView.HardNavigateTo(MauiWebView.BaseLocalUri.ToString());
    }
}
