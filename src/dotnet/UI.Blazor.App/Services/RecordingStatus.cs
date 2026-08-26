using ActualChat.UI.Blazor.Resources;
using Microsoft.Extensions.Localization;

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
    public string GetTooltip(IStringLocalizer l)
        => Kind switch {
            RecordingStatusKind.Starting => l.Call_StartingRecording,
            RecordingStatusKind.Recording => l.Call_Recording,
            RecordingStatusKind.Reconnecting or RecordingStatusKind.Disconnected => l.Reconnect_Title,
            RecordingStatusKind.NoMicrophonePermission => l.Recording_NoMicrophoneAccess,
            RecordingStatusKind.NoMicrophone => l.Recording_NoMicrophone,
            RecordingStatusKind.MicrophoneBusy => l.Recording_MicrophoneBusy,
            RecordingStatusKind.StartFailed => FailureCode is { } code
                ? l.Recording_UnknownError_Format(code)
                : l.Recording_UnknownError,
            _ => l.ChatMenu_StartRecording,
        };

    public static RecordingStatus Parse(string status)
    {
        // "<Kind>" or "<Kind>:<code>" - the form debugUI.forceRecordingStatus takes
        var separatorIndex = status.IndexOf(':');
        var name = separatorIndex < 0 ? status : status[..separatorIndex];
        var code = separatorIndex < 0 ? null : status[(separatorIndex + 1)..].NullIfEmpty();
        if (!Enum.TryParse<RecordingStatusKind>(name, true, out var kind))
            throw StandardError.Constraint($"Unknown recording status kind: '{name}'.");

        return new RecordingStatus(kind, code);
    }

    public static RecordingStatus From(RecordingFailure failure)
        => failure.Result switch {
            RecorderStartResult.NoPermission => new(RecordingStatusKind.NoMicrophonePermission),
            RecorderStartResult.NoDevice => new(RecordingStatusKind.NoMicrophone),
            RecorderStartResult.DeviceBusy => new(RecordingStatusKind.MicrophoneBusy),
            _ => new(RecordingStatusKind.StartFailed, failure.Code),
        };
}
