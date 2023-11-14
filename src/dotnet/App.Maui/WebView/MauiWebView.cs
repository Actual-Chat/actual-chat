// using WKWebView = global::WebKit.WKWebView;

using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public partial class MauiWebView(BlazorWebView blazorWebView, object platformWebView)
{
    private static readonly Tracer Tracer = Tracer.Default[nameof(MauiWebView)];
    private static readonly object StaticLock = new();
    private static MauiWebView? _current;
    private static int _lastId;

    public static MauiWebView? Current => _current;

    private readonly TaskCompletionSource _whenDeactivatedSource = TaskCompletionSourceExt.New();
    private static ILogger? _log;

    public BlazorWebView BlazorWebView { get; } = blazorWebView;
    public object PlatformWebView { get; } = platformWebView;

    public long Id { get; } = Interlocked.Increment(ref _lastId);
    public bool IsActive => _current == this;
    public Task WhenDeactivated => _whenDeactivatedSource.Task;
    public IServiceProvider? ScopedServices { get; private set; }
    public Session? Session { get; private set; }

    private object Lock => _whenDeactivatedSource;
    private ILogger Log => _log ??= AppServices.LogFor(GetType());

    public static MauiWebView Activate(object platformWebView)
    {
        MauiWebView? mauiWebView;
        lock (StaticLock) {
            mauiWebView = IfActive(platformWebView);
            if (mauiWebView != null)
                return mauiWebView; // Somehow already active

            _current?.Deactivate();
            mauiWebView = _current = new MauiWebView(MainPage.Current.BlazorWebView, platformWebView);
        }
        Tracer.Point($"Activate: #{mauiWebView.Id}");
        mauiWebView.OnHandlerConnected();
        return mauiWebView;
    }

    public void Deactivate()
    {
        bool wasDeactivated;
        lock (StaticLock) {
            if (_current == this)
                _current = null;

            wasDeactivated = _whenDeactivatedSource.TrySetResult();
        }
        if (!wasDeactivated)
            return;

        Tracer.Point($"Deactivate: #{Id}");
    }

    public static MauiWebView? IfActive(object webView)
    {
        var current = Current;
        return ReferenceEquals(current?.PlatformWebView, webView) ? current : null;
    }

    public static MauiWebView? IfActive(BlazorWebView blazorWebView)
    {
        var current = Current;
        return ReferenceEquals(current?.BlazorWebView, blazorWebView) ? current : null;
    }

    public MauiWebView? IfActive()
        => IsActive ? this : null;

    public partial void OnHandlerConnected();
    public partial void OnHandlerDisconnected();
    public partial void OnInitializing(BlazorWebViewInitializingEventArgs eventArgs);
    public partial void OnInitialized(BlazorWebViewInitializedEventArgs eventArgs);
    public partial void OnLoaded(EventArgs eventArgs);
    public partial Task EvaluateJavaScript(string javaScript);

    public void OnAttach(IServiceProvider scopedServices, Session session)
    {
        bool isSessionChanged;
        lock (Lock) {
            if (ReferenceEquals(ScopedServices, scopedServices))
                Log.LogWarning("OnAttach is called more than once for the same ScopedServices!");

            isSessionChanged = Session != session;
            ScopedServices = scopedServices;
            Session = session;
            scopedServices.GetRequiredService<Mutable<MauiWebView?>>().Value = this;
            AppServicesAccessor.ScopedServices = scopedServices;
        }
        if (isSessionChanged)
            SetupSessionCookie(session);
    }

    public void OnUrlLoading(UrlLoadingEventArgs eventArgs)
    {
        var uri = eventArgs.Url;
        OnUrlLoading(uri, eventArgs);
        Tracer.Point($"{nameof(OnUrlLoading)}: Url: '{uri}' -> {eventArgs.UrlLoadingStrategy}");
    }

    // Private methods

    private partial void SetupSessionCookie(Session session);
}
