using ActualChat.App.Maui.Services;
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
        MauiLoadingUI.MarkFirstWebViewCreated();
        RecreateWebView();
    }

    public void RecreateWebView()
    {
        var mauiWebView = new MauiWebView();
#if USE_MAUI_SPLASH
        Content = new Grid {
            new MauiSplash(),
            mauiWebView.BlazorWebView,
        };
#else
        Content = mauiWebView.BlazorWebView;
#endif
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
