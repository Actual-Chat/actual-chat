namespace ActualChat.App.Maui;

public class MainPage : ContentPage
{
    private static volatile MainPage _current = null!;

    public static MainPage Current => _current;

    public MainPage()
    {
        Interlocked.Exchange(ref _current, this);
        BackgroundColor = Color.FromRgb(0x44, 0x44, 0x44);
        RecreateWebView();
    }

    public void RecreateWebView()
        => Content = new MauiWebView().BlazorWebView;

    public void Reload()
    {
        return;
        var mauiWebView = MauiWebView.Current;
        if (mauiWebView == null || mauiWebView.IsDead)
            RecreateWebView();
        else
            mauiWebView.HardNavigateTo(MauiWebView.BaseLocalUri.ToString());
    }
}
