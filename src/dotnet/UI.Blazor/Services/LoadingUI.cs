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

    public LoadingUI(ILogger<LoadingUI> log, ITraceSession trace)
    {
        Log = log;
        Trace = trace;
        _whenLoadedSource = TaskSource.New<Unit>(true);
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult(default)) return;

        Log.LogDebug("MarkLoaded");
        Trace.Track("MarkLoaded");
    }
}
