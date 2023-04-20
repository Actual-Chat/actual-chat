namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Delays splash screen removal in MAUI app.
/// </summary>
public sealed class LoadingUI
{
    private readonly TaskCompletionSource<Unit> _whenLoadedSource = TaskCompletionSourceExt.New<Unit>();

    private ILogger Log { get; }
    private Tracer Tracer { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;

    public TimeSpan LoadingTime { get; private set; } = TimeSpan.Zero;
    public static TimeSpan MauiAppBuildTime { get; private set; } = TimeSpan.Zero;
    public TimeSpan AppInitTime { get; private set; } = TimeSpan.Zero;
    public TimeSpan AppAboutRenderContentTime { get; private set; } = TimeSpan.Zero;
    public TimeSpan ChatListLoaded { get; private set; } = TimeSpan.Zero;

    public LoadingUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Tracer = services.Tracer(GetType());
    }

    public static void ReportMauiAppBuildTime(TimeSpan mauiAppBuildTime)
    {
        if (MauiAppBuildTime > TimeSpan.Zero)
            return;
        MauiAppBuildTime = mauiAppBuildTime;
    }

    public void ReportAppInitialized()
    {
        if (AppInitTime > TimeSpan.Zero)
            return;
        AppInitTime = Tracer.Elapsed;
    }

    public void ReportAppAboutRenderContent()
    {
        if (AppAboutRenderContentTime > TimeSpan.Zero)
            return;
        AppAboutRenderContentTime = Tracer.Elapsed;
    }

    public void ReportChatListLoaded()
    {
        if (ChatListLoaded > TimeSpan.Zero)
            return;
        ChatListLoaded = Tracer.Elapsed;
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult(default)) return;

        Log.LogDebug(nameof(MarkLoaded));
        Tracer.Point(nameof(MarkLoaded));
        LoadingTime = Tracer.Elapsed;
    }
}
