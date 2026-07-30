using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public sealed partial class MauiWebView
{
    private static readonly Tracer Tracer = Tracer.Default[nameof(MauiWebView)];
    private static readonly Lock StaticLock = new();
    private static MauiWebView? _current;
    private static int _lastId;
    private static long _lastResumeAt = CpuTimestamp.Now.Value;
    private static ILogger? _log;

    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<MainPage>();

    public static MauiWebView? Current => _current;
    public static CpuTimestamp LastResumeAt => new(Interlocked.Read(ref _lastResumeAt));

    private readonly Lock _lock = new();
    private MauiWebViewPageContextTracker? _pageContextTracker;
    public long Id { get; }
    public BlazorWebView BlazorWebView { get; }
    public object PlatformWebView { get; private set; } = null!;
    public IServiceProvider? ScopedServices { get; private set; }
    public Session? Session { get; private set; }
    public bool IsDead { get; private set; }

    public MauiWebView()
    {
        Id = Interlocked.Increment(ref _lastId);
        if (Id > 1)
            AppNavigationQueue.Reset();

        BlazorWebView = new BlazorWebView {
            HostPage = "wwwroot/index.html",
            BackgroundColor = Colors.Yellow, // DIAGNOSTIC
        };
        BlazorWebView.BlazorWebViewInitializing += OnInitializing;
        BlazorWebView.BlazorWebViewInitialized += OnInitialized;
        BlazorWebView.UrlLoading += OnLoading;
        BlazorWebView.Loaded += OnLoaded;
        BlazorWebView.Unloaded += OnUnloaded;
        BlazorWebView.RootComponents.Add(
            new RootComponent {
                ComponentType = typeof(HeadOutlet),
                Selector = "head::after",
            });
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

    public Task SetScopedServices(IServiceProvider scopedServices, Session session)
    {
        bool isSessionChanged;
        lock (_lock) {
            if (ReferenceEquals(ScopedServices, scopedServices))
                return Task.CompletedTask;

            isSessionChanged = Session != session;
            ScopedServices = scopedServices;
            Session = session;
            BlazorAppServices = scopedServices;
        }
        return isSessionChanged
            ? SetupCookies(session)
            : Task.CompletedTask;
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

            ScopedServices = null;
            DiscardBlazorAppServices(scopedServices, "MauiWebView.ResetScopedServices");
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

    public Task EvaluateJS(string code)
        => DispatchToMainThread(() => EvaluateJSInternal(code));

    private partial Task EvaluateJSInternal(string code);

    public void Disconnect()
    {
        _pageContextTracker?.OnMauiWebViewDisconnect();
        DisconnectHandler();
    }

    // Private methods

    private partial void OnInitializing(object? sender, BlazorWebViewInitializingEventArgs eventArgs);
    private partial void OnInitialized(object? sender, BlazorWebViewInitializedEventArgs eventArgs);
    private partial void OnLoaded(object? sender, EventArgs eventArgs);
    private partial Task SetupCookies(Session session);

    public bool HasDisconnected { get; private set; }

    private void DisconnectHandler()
    {
        // Calling disconnect should no logger cause UI hanging.
        // Fixed in https://github.com/dotnet/maui/pull/25430.
        var viewHandler = BlazorWebView.Handler;
        if (viewHandler is null) {
            Log.LogInformation("-> DisconnectHandler. Skipping: no handler");
            return;
        }

        try {
            Log.LogInformation("-> DisconnectHandler");
            HasDisconnected = true;
            viewHandler.DisconnectHandler();
            Log.LogInformation("<- DisconnectHandler");
        }
        catch (Exception e) {
            Log.LogError(e, "<- DisconnectHandler failed");
        }
    }

    private void OnUnloaded(object? sender, EventArgs eventArgs)
    {
        Log.LogInformation("OnUnloaded: Disconnecting BlazorWebView.Handler");
        DisconnectHandler();
    }

    internal void LinkTo(MauiWebViewPageContextTracker mauiWebViewPageContextTracker)
        => _pageContextTracker = mauiWebViewPageContextTracker;
}
