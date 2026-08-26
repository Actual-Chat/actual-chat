namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// How recording in a chat is going, as the UI reports it. A problem stays
/// <see cref="RecordingStatusKind.Starting"/> or <see cref="RecordingStatusKind.Reconnecting"/>
/// until it outlasts <see cref="Constants.Audio.RecordingProblemGracePeriod"/>, unless the
/// pipeline named it - a named failure is reported at once.
/// </summary>
public sealed record RecordingStatus(RecordingStatusKind Kind, string? FailureCode = null)
{
    public static readonly RecordingStatus Off = new(RecordingStatusKind.Off);
    public static readonly RecordingStatus Starting = new(RecordingStatusKind.Starting);
    public static readonly RecordingStatus Recording = new(RecordingStatusKind.Recording);
    public static readonly RecordingStatus Reconnecting = new(RecordingStatusKind.Reconnecting);
    public static readonly RecordingStatus Disconnected = new(RecordingStatusKind.Disconnected);
    public static readonly RecordingStatus StartFailed = new(RecordingStatusKind.StartFailed);
    public bool IsFailure => Kind >= RecordingStatusKind.Disconnected;
    public static RecordingStatus From(RecordingFailure failure)
        => failure.Result switch {
            RecorderStartResult.NoPermission => new(RecordingStatusKind.NoMicrophonePermission),
            RecorderStartResult.NoDevice => new(RecordingStatusKind.NoMicrophone),
            RecorderStartResult.DeviceBusy => new(RecordingStatusKind.MicrophoneBusy),
            _ => new(RecordingStatusKind.StartFailed, failure.Code),
        };
}
