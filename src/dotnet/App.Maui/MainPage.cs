using ActualChat.UI.Blazor.Services;
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
        BackgroundColor = Color.FromRgb(0x44, 0x44, 0x44);
        On<iOS>().SetUseSafeArea(true);
    }

    public void RecreateWebView()
        => Content = new Grid {
            new SplashOverlay(),
            new MauiWebView().BlazorWebView,
        };

    public void Reload()
    {
        var mauiWebView = MauiWebView.Current;
        if (mauiWebView == null || mauiWebView.IsDead)
            RecreateWebView();
        else
            mauiWebView.HardNavigateTo(MauiWebView.BaseLocalUri.ToString());
    }
}
