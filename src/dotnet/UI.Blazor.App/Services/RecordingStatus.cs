namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// How recording in a chat is going, as the UI reports it. A problem is
/// <see cref="Starting"/> or <see cref="Reconnecting"/> until it outlasts
/// <see cref="Constants.Audio.RecordingProblemGracePeriod"/>, and a failure after that.
/// </summary>
public enum RecordingStatus
{
    Off,
    Starting,
    Recording,
    Reconnecting,
    StartFailed,
    Disconnected,
}
