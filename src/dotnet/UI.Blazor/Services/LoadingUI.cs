namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Delays splash screen removal in MAUI app.
/// </summary>
public sealed class LoadingUI
{
    private readonly TaskCompletionSource<Unit> _whenLoadedSource = TaskCompletionSourceExt.New<Unit>();
    private readonly TaskCompletionSource<Unit> _whenChatListLoadedSource = TaskCompletionSourceExt.New<Unit>();

    private ILogger Log { get; }
    private Tracer Tracer { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;
    public Task WhenChatListLoaded => _whenChatListLoadedSource.Task;

    public TimeSpan LoadingTime { get; private set; }
    public static TimeSpan MauiAppBuildTime { get; private set; }
    public TimeSpan AppInitializeTime { get; private set; }
    public TimeSpan AppReadyToRenderContentTime { get; private set; }
    public TimeSpan ChatListLoadTime { get; private set; }

    public LoadingUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Tracer = services.Tracer(GetType());
    }

    public static void MarkMauiAppBuilt(TimeSpan mauiAppBuildTime)
    {
        if (MauiAppBuildTime == default)
            MauiAppBuildTime = mauiAppBuildTime;
    }

    public void MarkAppInitialized()
    {
        if (AppInitializeTime == default)
            AppInitializeTime = Tracer.Elapsed;
    }

    public void MarkAppReadyToRenderContent()
    {
        if (AppReadyToRenderContentTime == default)
            AppReadyToRenderContentTime = Tracer.Elapsed;
    }

    public void MarkChatListLoaded()
    {
        if (!_whenChatListLoadedSource.TrySetResult(default))
            return;

        ChatListLoadTime = Tracer.Elapsed;
        Log.LogDebug(nameof(MarkChatListLoaded));
        Tracer.Point(nameof(MarkChatListLoaded));
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult(default))
            return;

        LoadingTime = Tracer.Elapsed;
        Log.LogDebug(nameof(MarkLoaded));
        Tracer.Point(nameof(MarkLoaded));
    }
}
