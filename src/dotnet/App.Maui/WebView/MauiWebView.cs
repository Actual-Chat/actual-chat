// using WKWebView = global::WebKit.WKWebView;

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public sealed partial class MauiWebView
{
    private static readonly Tracer Tracer = Tracer.Default[nameof(MauiWebView)];
    private static readonly object StaticLock = new();
    private static MauiWebView? _current;
    private static int _lastId;
    private static long _lastResumeAt = CpuTimestamp.Now.Value;
    private static ILogger? _log;

    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<MainPage>();

    public static MauiWebView? Current => _current;
    public static CpuTimestamp LastResumeAt => new(Interlocked.Read(ref _lastResumeAt));

    private readonly object _lock = new();
    public long Id { get; }
    public BlazorWebView BlazorWebView { get; }
    public object PlatformWebView { get; private set; } = null!;
    public IServiceProvider? ScopedServices { get; private set; }
    public Session? Session { get; private set; }
    public bool IsDead { get; private set; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiWebView))]
    public MauiWebView()
    {
        Id = Interlocked.Increment(ref _lastId);
        Console.WriteLine($"MauiWebView: #{Id}");
        BlazorWebView = new BlazorWebView {
            HostPage = "wwwroot/index.html",
            BackgroundColor = MauiSettings.SplashBackgroundColor,
        };
        BlazorWebView.BlazorWebViewInitializing += OnInitializing;
        BlazorWebView.BlazorWebViewInitialized += OnInitialized;
        BlazorWebView.UrlLoading += OnLoading;
        BlazorWebView.Loaded += OnLoaded;
        BlazorWebView.Unloaded += OnUnloaded;
        BlazorWebView.RootComponents.Add(
            new RootComponent {
                ComponentType = typeof(MauiBlazorApp),
                Selector = "#app",
            });
        lock (StaticLock)
            _current = this;
        Tracer.Point($"Current = #{Id}");
    }

    public static bool IsCurrent(object platformWebView, [NotNullWhen(true)] out MauiWebView? mauiWebView)
    {
        if (ReferenceEquals(Current?.PlatformWebView, platformWebView)) {
            mauiWebView = Current;
            return true;
        }

        mauiWebView = null;
        return false;
    }

    public static void LogResume()
    {
        Interlocked.Exchange(ref _lastResumeAt, CpuTimestamp.Now.Value);
        Log.LogInformation("Resume logged");
    }

    public partial void SetPlatformWebView(object platformWebView);

    public void SetScopedServices(IServiceProvider scopedServices, Session session)
    {
        bool isSessionChanged;
        lock (_lock) {
            if (ReferenceEquals(ScopedServices, scopedServices))
                return;

            isSessionChanged = Session != session;
            ScopedServices = scopedServices;
            Session = session;
            scopedServices.GetRequiredService<Mutable<MauiWebView?>>().Value = this;
            AppServicesAccessor.ScopedServices = scopedServices;
        }
        if (isSessionChanged)
            SetupSessionCookie(session);
    }

    public void ResetScopedServices(IServiceProvider scopedServices)
    {
        lock (_lock) {
            if (ScopedServices == null)
                return;
            if (!ReferenceEquals(ScopedServices, scopedServices)) {
                Log.LogWarning($"{nameof(ResetScopedServices)} is called w/ wrong ScopedServices instance!");
                return;
            }

            try {
                scopedServices.GetRequiredService<Mutable<MauiWebView?>>().Value = null;
            }
            catch {
                // Intended, may fail on dispose
            }
            ScopedServices = null;
        }
    }

    public bool MarkDead()
    {
        lock (_lock) {
            if (IsDead)
                return false;

            IsDead = true;
        }
        Log.LogInformation("WebView is dead");
        return true;
    }

    public partial void HardNavigateTo(string url);
    public partial Task EvaluateJavaScript(string javaScript);

    // Private methods

    private partial void OnInitializing(object? sender, BlazorWebViewInitializingEventArgs eventArgs);
    private partial void OnInitialized(object? sender, BlazorWebViewInitializedEventArgs eventArgs);
    private partial void OnLoaded(object? sender, EventArgs eventArgs);
    private partial void SetupSessionCookie(Session session);

    private void OnUnloaded(object? sender, EventArgs eventArgs)
    {
        // BlazorWebView.Handler?.DisconnectHandler();
        // It hangs the app on Windows, Android, iOS due to a deadlock described in a workaround below.

        if (BlazorWebView.Handler is not BlazorWebViewHandler handler)
            return;

        if (handler.GetWebViewManager() is { } webViewManager)
            _ = MainThread.InvokeOnMainThreadAsync(async () => {
                // BlazorWebView.Handler.DisconnectHandler synchronously waits for DisposeAsync task completion,
                // which may cause a deadlock on the main thread. We workaround it by:
                // - Deactivating webView synchronously
                // - Starting to dispose Handler._webViewManager in the main thread
                // - Once it completes, we call DisconnectHandler, which shouldn't dispose anything at that point.
                //
                // See:
                // - https://github.com/dotnet/maui/blob/main/src/BlazorWebView/src/Maui/Windows/BlazorWebViewHandler.Windows.cs#L35
                // - https://github.com/dotnet/maui/blob/main/src/BlazorWebView/src/Maui/Android/BlazorWebViewHandler.Android.cs#L70
                // - https://github.com/dotnet/aspnetcore/blob/main/src/Components/WebView/WebView/src/WebViewManager.cs#L264
                // - https://github.com/dotnet/aspnetcore/blob/main/src/Components/WebView/WebView/src/PageContext.cs#L58
                var pageContext = webViewManager.GetCurrentPageContext();
                webViewManager.ResetCurrentPageContext();
                if (pageContext != null) {
                    try {
                        Log.LogInformation("OnUnloaded: Disposing PageContext");
                        await pageContext.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Log.LogError(e, "OnUnloaded: PageContext.DisposeAsync() failed");
                    }
                }
                if (BlazorWebView.Handler is { } viewHandler) {
                    try {
                        Log.LogInformation("OnUnloaded: Disconnecting BlazorWebView.Handler");
                        viewHandler.DisconnectHandler();
                    }
                    catch (Exception e) {
                        Log.LogError(e, "OnUnloaded: BlazorWebView.Handler.DisconnectHandler() failed");
                    }
                }
            });
    }
}
