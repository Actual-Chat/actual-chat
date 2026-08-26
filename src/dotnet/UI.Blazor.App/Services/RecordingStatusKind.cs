namespace ActualChat.UI.Blazor.App.Services;

public enum RecordingStatusKind
{
    Off = 0,
    Starting,
    Recording,
    Reconnecting,
    // Everything from here on is a failure - see RecordingStatus.IsFailure
    Disconnected,
    NoMicrophonePermission,
    NoMicrophone,
    MicrophoneBusy,
    StartFailed,
}
