namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Used to delay splash screen removing in MAUI app.
/// </summary>
public class AppIsReadyMarker
{
    private readonly TaskCompletionSource _taskCompletionSource = new ();

    private ILogger<AppIsReadyMarker> Log { get; }

    public AppIsReadyMarker(ILogger<AppIsReadyMarker> log)
        => Log = log;

    /// <summary>
    /// Indicates that app is ready for presenting. Splash screen should be removed.
    /// </summary>
    public bool IsReady
        => WhenReady.IsCompletedSuccessfully;

    public Task WhenReady
        => _taskCompletionSource.Task;

    public void Set()
    {
        if (_taskCompletionSource.TrySetResult())
            Log.LogDebug("AppIsReadyMarker is set");
    }
}
