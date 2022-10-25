namespace ActualChat.UI.Blazor;

/// <summary>
/// Used to delay splash screen removing in MAUI app.
/// </summary>
public class AppIsReadyMarker
{
    /// <summary>
    /// Indicates that app is ready for presenting. Splash screen should be removed.
    /// </summary>
    public bool IsReady { get; private set; }

    public void Set()
        => IsReady = true;
}
