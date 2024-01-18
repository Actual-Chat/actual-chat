using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Keeps splash screen / splash UI open in WASM & MAUI.
/// </summary>
public class LoadingUI
{
    private static readonly Tracer StaticTracer = Tracer.Default[nameof(LoadingUI)];
    private static readonly TaskCompletionSource _whenAppRenderedSource = new();

    public static TimeSpan AppCreationTime { get; private set; }
    public static TimeSpan AppBuildTime { get; private set; }
    public static Task WhenAppRendered => _whenAppRenderedSource.Task;

    private readonly TaskCompletionSource _whenLoadedSource = new();
    private readonly TaskCompletionSource _whenRenderedSource = new();
    private readonly TaskCompletionSource _whenChatListLoadedSource = new();
    private volatile int _isWebSplashRemoved;

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
        HostInfo = Services.HostInfo();
        if (HostInfo.HostKind.IsMauiApp() && StaticTracer.Elapsed < TimeSpan.FromSeconds(10)) {
            // This is to make sure first scope's timings in MAUI are relative to app start
            Tracer = StaticTracer[GetType()];
        }
        else
            Tracer = services.Tracer(GetType());
    }

    public static void MarkAppBuilt()
    {
        if (AppBuildTime != default)
            return;

        AppBuildTime = StaticTracer.Elapsed;
        StaticTracer.Point();
    }

    public static void MarkAppCreated()
    {
        if (AppCreationTime != default)
            return;

        AppCreationTime = StaticTracer.Elapsed;
        StaticTracer.Point();
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult())
            return;

        LoadTime = Tracer.Elapsed;
        Tracer.Point();

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
        Tracer.Point();
        _whenAppRenderedSource.TrySetResult();
        RemoveWebSplash();
    }

    public void MarkChatListLoaded()
    {
        if (!_whenChatListLoadedSource.TrySetResult())
            return;

        ChatListLoadTime = Tracer.Elapsed;
        Tracer.Point();
    }

    public void RemoveWebSplash(bool instantly = false)
    {
        if (_isWebSplashRemoved != 0)
            return;

        _ = ForegroundTask.Run(async () => {
                await Services.JSRuntime()
                    .InvokeVoidAsync(
                        $"{BlazorUICoreModule.ImportName}.BrowserInit.removeWebSplash",
                        instantly
                    ).ConfigureAwait(false);
                Interlocked.Exchange(ref _isWebSplashRemoved, 1);
            },
            e => Services.LogFor<LoadingUI>().LogError(e, "RemoveWebSplash failed"));
    }
}
