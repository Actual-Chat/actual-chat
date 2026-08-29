namespace ActualChat.App.Maui;

public sealed partial class MainPage : ContentPage
{
    // Past this the WebView waits for the container itself - the old behaviour - rather than
    // leaving a blank splash on screen forever.
    private static readonly TimeSpan MaxAttachDelay = TimeSpan.FromSeconds(10);
    private static MainPage _current = null!;
    private int _isWebViewAttachPending = 1;
    public static MainPage Current => Volatile.Read(ref _current);
    public bool IsWebViewAttachPending
        // Tells "no WebView yet, still starting up" from "it went away while backgrounded".
        => Volatile.Read(ref _isWebViewAttachPending) != 0;

    public MainPage()
    {
        Interlocked.Exchange(ref _current, this);

        // Safe areas handled by CSS via viewport-fit=cover and env(safe-area-inset-*)
        BackgroundColor = MauiSettings.SplashBackgroundColor;
        _ = AttachWebViewWhenReady();

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

    // Private methods

    private void OnLoaded(object? sender, EventArgs e)
        => OnLoaded_Platform();

    private void OnUnloaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }

    private async Task AttachWebViewWhenReady()
    {
        // Off the UI thread on purpose: the BlazorWebView ctor blocks it on Chromium's provider
        // lock, and the handler then waits there for the container. The background stands in.
        var whenReady = Task.WhenAll(BlazorWebViewApp.WhenAppReady, WhenPlatformWebViewReady());
        await whenReady.WaitAsync(MaxAttachDelay).SilentAwait(false);
        Volatile.Write(ref _isWebViewAttachPending, 0);
        if (!ReferenceEquals(Volatile.Read(ref _current), this))
            return; // A newer MainPage took over while we were waiting

        BeginDispatchToMainThread(RecreateWebView);
    }

    private partial void OnLoaded_Platform();
    private partial Task WhenPlatformWebViewReady();
}
