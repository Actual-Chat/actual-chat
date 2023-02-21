namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Delays splash screen removal in MAUI app.
/// </summary>
public sealed class LoadingUI
{
    private readonly TaskSource<Unit> _whenLoadedSource;

    private ILogger<LoadingUI> Log { get; }
    private ITraceSession Trace { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;

    public TimeSpan LoadingTime { get; private set; } = TimeSpan.Zero;
    public static TimeSpan MauiAppBuildTime { get; private set; } = TimeSpan.Zero;
    public TimeSpan AppInitTime { get; private set; } = TimeSpan.Zero;
    public TimeSpan AppAboutRenderContentTime { get; private set; } = TimeSpan.Zero;

    public LoadingUI(ILogger<LoadingUI> log, ITraceSession trace)
    {
        Log = log;
        Trace = trace;
        _whenLoadedSource = TaskSource.New<Unit>(true);
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
        AppInitTime = Trace.Elapsed;
    }

    public void ReportAppAboutRenderContent()
    {
        if (AppAboutRenderContentTime > TimeSpan.Zero)
            return;
        AppAboutRenderContentTime = Trace.Elapsed;
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult(default)) return;

        Log.LogDebug("MarkLoaded");
        Trace.Track("MarkLoaded");
        LoadingTime = Trace.Elapsed;
    }
}
