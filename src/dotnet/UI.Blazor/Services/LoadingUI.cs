using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Keeps the splash screen / splash UI open in WASM & MAUI.
/// </summary>
public class LoadingUI : UIServiceBase<UIHub>
{
    private static readonly Tracer StaticTracer = Tracer.Default[nameof(LoadingUI)];
    // ReSharper disable once InconsistentNaming
    private static readonly AsyncTaskMethodBuilder _whenAppRenderedSource = AsyncTaskMethodBuilderExt.New();

    public static TimeSpan AppCreationTime { get; private set; }
    public static TimeSpan AppBuildTime { get; private set; }
    public static Task WhenAppRendered => _whenAppRenderedSource.Task;

    private readonly AsyncTaskMethodBuilder _whenLoadedSource = AsyncTaskMethodBuilderExt.New();
    private readonly AsyncTaskMethodBuilder _whenRenderedSource = AsyncTaskMethodBuilderExt.New();
    private readonly AsyncTaskMethodBuilder _whenChatListLoadedSource = AsyncTaskMethodBuilderExt.New();
    private volatile int _isWebSplashRemoved;

    private new Tracer Tracer { get; }

    public TimeSpan LoadTime { get; private set; }
    public TimeSpan RenderTime { get; private set; }
    public TimeSpan ChatListLoadTime { get; private set; }
    public Task WhenLoaded => _whenLoadedSource.Task;
    public Task WhenRendered => _whenRenderedSource.Task;
    public Task WhenChatListLoaded => _whenChatListLoadedSource.Task;

    public LoadingUI(UIHub hub) : base(hub)
    {
        if (HostInfo.HostKind.IsMauiApp() && StaticTracer.Elapsed < TimeSpan.FromSeconds(10)) {
            // This is to make sure the first scope's timings in MAUI are relative to app start
            Tracer = StaticTracer[GetType()];
        }
        else
            Tracer = Hub.TracerFor(GetType());
    }

    public static void MarkAppBuilt()
    {
        if (AppBuildTime != default)
            return;

        AppBuildTime = StaticTracer.Elapsed;
        StaticTracer.MethodPoint();
    }

    public static void MarkAppCreated()
    {
        if (AppCreationTime != default)
            return;

        AppCreationTime = StaticTracer.Elapsed;
        StaticTracer.MethodPoint();
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult())
            return;

        LoadTime = Tracer.Elapsed;
        Tracer.MethodPoint();

        // We want to make sure MarkRendered is called no matter what (e.g., even if render fails)
        _ = Task.Delay(TimeSpan.FromSeconds(0.5)).ContinueWith(
            _ => MarkRendered(),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public void MarkRendered()
    {
        if (!_whenRenderedSource.TrySetResult())
            return;

        RenderTime = Tracer.Elapsed;
        Tracer.MethodPoint();
        _whenAppRenderedSource.TrySetResult();

        // A native splash still covers the screen here, so the fade would be invisible - and this
        // removal is what releases it. IsMauiApp matters: OSInfo is the .NET runtime's OS, so a
        // Windows-hosted Blazor Server / Auto session reports IsWindows with no native splash at all.
        var isCoveredByNativeSplash = HostInfo.HostKind.IsMauiApp() && (OSInfo.IsAndroid || OSInfo.IsWindows);
        RemoveWebSplash(isCoveredByNativeSplash);
    }

    public void MarkChatListLoaded()
    {
        if (!_whenChatListLoadedSource.TrySetResult())
            return;

        ChatListLoadTime = Tracer.Elapsed;
        Tracer.MethodPoint();
    }

    public void RemoveWebSplash(bool instantly = false)
    {
        if (_isWebSplashRemoved != 0)
            return;

        _ = ForegroundTask.Run(async () => {
                await JS.InvokeVoidAsync(
                        $"{BlazorUICoreModule.ImportName}.BrowserInit.removeWebSplash",
                        instantly)
                    .ConfigureAwait(false);
                Interlocked.Exchange(ref _isWebSplashRemoved, 1);
            },
            e => Log.LogError(e, "RemoveWebSplash failed"));
    }
}
