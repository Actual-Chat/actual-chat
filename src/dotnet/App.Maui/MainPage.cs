using ActualChat.Maui;
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
#if ANDROID
        // .NET 10 changed ContentPage to default to SafeAreaEdges.None (edge-to-edge) on Android.
        // Set to All to avoid system bars (status bar, navigation bar) overlapping content.
        SafeAreaEdges = SafeAreaEdges.All;
#endif
        BackgroundColor = MauiSettings.SplashBackgroundColor;
        RecreateWebView();
    }

    public void RecreateWebView()
    {
        var oldWebView = MauiWebView.Current;
        Content = null;
        oldWebView?.Disconnect();
        Content = new MauiWebView().BlazorWebView;
    }

    // NOTE(AY): Currently unused
    public void Reload()
    {
        var mauiWebView = MauiWebView.Current;
        if (mauiWebView == null || mauiWebView.IsDead)
            RecreateWebView();
        else
            mauiWebView.HardNavigateTo(MauiWebView.BaseLocalUri.ToString());
    }

    public void Unload()
        => Content = null;
}
