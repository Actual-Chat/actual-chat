namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Why the JS video recorder failed, so <see cref="ChatVideoUI"/> can pick the catalog string.
/// Values mirror <c>VideoRecorderError</c> in video-recorder.ts.
/// </summary>
public enum VideoRecorderError
{
    Unknown = 0,
    CameraUnavailable = 1,
    RestartRequired = 2,
}
