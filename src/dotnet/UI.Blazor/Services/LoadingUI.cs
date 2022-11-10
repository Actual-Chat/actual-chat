namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Delays splash screen removal in MAUI app.
/// </summary>
public sealed class LoadingUI
{
    private readonly TaskSource<Unit> _whenLoadedSource;

    private ILogger<LoadingUI> Log { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;

    public LoadingUI(ILogger<LoadingUI> log)
    {
        Log = log;
        _whenLoadedSource = TaskSource.New<Unit>(true);
    }

    public void MarkLoaded()
    {
        if (_whenLoadedSource.TrySetResult(default))
            Log.LogDebug("MarkLoaded");
    }
}
