using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Keeps splash screen / splash UI open in WASM & MAUI.
/// </summary>
public sealed class LoadingUI
{
    private static readonly Tracer StaticTracer = Tracer.Default[nameof(LoadingUI)];
    private static readonly TaskCompletionSource _whenViewCreatedSource = new();
    private static readonly TaskCompletionSource _whenAppRenderedSource = new();

    public static TimeSpan AppCreationTime { get; private set; }
    public static TimeSpan AppBuildTime { get; private set; }
    public static Task WhenViewCreated => _whenViewCreatedSource.Task;
    public static Task WhenAppRendered => _whenAppRenderedSource.Task;

    private readonly TaskCompletionSource _whenLoadedSource = new();
    private readonly TaskCompletionSource _whenRenderedSource = new();
    private readonly TaskCompletionSource _whenChatListLoadedSource = new();
    private bool _isLoadingOverlayShown;

    private IServiceProvider Services { get; }
    private HostInfo HostInfo { get; }
    private Tracer Tracer { get; }

    public TimeSpan LoadTime { get; private set; }
    public TimeSpan RenderTime { get; private set; }
    public TimeSpan ChatListLoadTime { get; private set; }
    public Task WhenLoaded => _whenLoadedSource.Task;
    public Task WhenRendered => _whenRenderedSource.Task;
    public Task WhenChatListLoaded => _whenChatListLoadedSource.Task;

    public LoadingUI(IServiceProvider services)
    {
        Services = services;
        Tracer = services.Tracer(GetType());
        HostInfo = Services.GetRequiredService<HostInfo>();
        var appKind = HostInfo.AppKind;
        if (appKind.IsMauiApp()) {
            if (!OSInfo.IsIOS)
                ShowLoadingOverlay();

            if (StaticTracer.Elapsed < TimeSpan.FromSeconds(10)) {
                // This is to make sure first scope's timings in MAUI are relative to app start
                Tracer = StaticTracer[GetType()];
            }
        }
        else if (appKind.IsClient())
            ShowLoadingOverlay();
    }

    public static void MarkAppBuilt()
    {
        if (AppBuildTime != default)
            return;

        AppBuildTime = StaticTracer.Elapsed;
        StaticTracer.Point(nameof(MarkAppBuilt));
    }

    public static void MarkViewCreated()
    {
        if (!_whenViewCreatedSource.TrySetResult())
            return;

        StaticTracer.Point(nameof(MarkViewCreated));
    }

    public static void MarkAppCreated()
    {
        if (AppCreationTime != default)
            return;

        AppCreationTime = StaticTracer.Elapsed;
        StaticTracer.Point(nameof(MarkAppCreated));
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult())
            return;

        LoadTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkLoaded));

        // We want to make sure MarkRendered is called no matter what (e.g. even if render fails)
        _ = Task.Delay(TimeSpan.FromSeconds(0.5)).ContinueWith(
            _ => MarkRendered(),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public void MarkRendered()
    {
        if (!_whenRenderedSource.TrySetResult())
            return;

        RenderTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkRendered));
        _whenAppRenderedSource.TrySetResult();
        HideLoadingOverlay();
    }

    public void MarkChatListLoaded()
    {
        if (!_whenChatListLoadedSource.TrySetResult())
            return;

        ChatListLoadTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkChatListLoaded));
    }

    // Private methods

    private void ShowLoadingOverlay()
    {
        if (_isLoadingOverlayShown)
            return;

        _isLoadingOverlayShown = true;
        var js = Services.JSRuntime();
        // We want to do this via script, coz BrowserInit might not be loaded yet
        const string script = """
        (function() {
            const overlay = document.getElementById('until-ui-is-ready');
            if (overlay) overlay.classList.remove('hidden');
        })();
        """;
        _ = js.EvalVoid(script);
    }

    private void HideLoadingOverlay()
    {
        if (!_isLoadingOverlayShown)
            return;

        _isLoadingOverlayShown = false;
        var js = Services.JSRuntime();
        // We want to do this via script, coz BrowserInit might not be loaded yet
        const string script = """
        (function() {
            const overlay = document.getElementById('until-ui-is-ready');
            if (overlay) overlay.classList.add('hidden');
        })();
        """;
        _ = js.EvalVoid(script);
    }
}
