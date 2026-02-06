using ActualChat.Maui;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace ActualChat.App.Maui;

public partial class MainPage : ContentPage
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
        SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.All);
#endif
        BackgroundColor = MauiSettings.SplashBackgroundColor;
        RecreateWebView();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

    private void OnLoaded(object? sender, EventArgs e)
        => OnLoaded_Platform();

    private void OnUnloaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }

    private partial void OnLoaded_Platform();
}
