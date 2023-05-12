using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Keeps splash screen / splash UI open in WASM & MAUI.
/// </summary>
public sealed class LoadingUI
{
    private static readonly Tracer StaticTracer = Tracer.Default[nameof(LoadingUI)];
    private static readonly TaskCompletionSource<Unit> _whenAppDisplayedSource = TaskCompletionSourceExt.New<Unit>();

    public static TimeSpan AppBuildTime { get; private set; }
    public static TimeSpan AppCreationTime { get; private set; }
    public static TimeSpan AppDisplayTime { get; private set; }
    public static Task WhenAppDisplayed => _whenAppDisplayedSource.Task;

    private readonly TaskCompletionSource<Unit> _whenLoadedSource = TaskCompletionSourceExt.New<Unit>();
    private readonly TaskCompletionSource<Unit> _whenChatListLoadedSource = TaskCompletionSourceExt.New<Unit>();
    private bool _isLoadingOverlayRemoved;

    private IServiceProvider Services { get; }
    private HostInfo HostInfo { get; }
    private Tracer Tracer { get; }

    public TimeSpan LoadTime { get; private set; }
    public TimeSpan ChatListLoadTime { get; private set; }
    public Task WhenLoaded => _whenLoadedSource.Task;
    public Task WhenChatListLoaded => _whenChatListLoadedSource.Task;

    public LoadingUI(IServiceProvider services)
    {
        Services = services;
        Tracer = services.Tracer(GetType());
        HostInfo = Services.GetRequiredService<HostInfo>();
        if (HostInfo.AppKind.IsMauiApp() && StaticTracer.Elapsed < TimeSpan.FromSeconds(10)) {
            // This is to make sure first scope's timings in MAUI are relative to app start
            Tracer = StaticTracer[GetType()];
        }

        // Let's we remove loading overlay no matter what after 10 seconds
        Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => RemoveLoadingOverlay(), TaskScheduler.Default);
    }

    public static void MarkAppBuilt()
    {
        if (AppBuildTime != default)
            return;

        AppBuildTime = StaticTracer.Elapsed;
        StaticTracer.Point(nameof(MarkAppBuilt));
    }

    public static void MarkAppCreated()
    {
        if (AppCreationTime != default)
            return;

        AppCreationTime = StaticTracer.Elapsed;
        StaticTracer.Point(nameof(MarkAppCreated));
    }

    public static void MarkAppDisplayed()
    {
        if (!_whenAppDisplayedSource.TrySetResult(default))
            return;

        AppDisplayTime = StaticTracer.Elapsed;
        StaticTracer.Point(nameof(MarkAppDisplayed));
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult(default))
            return;

        LoadTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkLoaded));
        RemoveLoadingOverlay();
    }

    public void MarkChatListLoaded()
    {
        if (!_whenChatListLoadedSource.TrySetResult(default))
            return;

        ChatListLoadTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkChatListLoaded));
    }

    // Private methods

    private void RemoveLoadingOverlay()
    {
        if (_isLoadingOverlayRemoved)
            return;

        _isLoadingOverlayRemoved = true;
        var js = Services.GetRequiredService<IJSRuntime>();
        const string script = """
        (function() {
            const overlay = document.getElementById('until-ui-is-ready');
            if (overlay) overlay.remove();
        })();
        """;
        js.EvalVoid(script);
    }
}
