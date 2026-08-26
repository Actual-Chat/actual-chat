namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// The last recording failure, kept on <see cref="AudioRecorderState"/> so the UI can name it.
/// It survives the recorder's restart loop and is cleared only once recording actually starts.
/// </summary>
public sealed record RecordingFailure(
    ChatId ChatId,
    RecorderStartResult Result,
    string? Code,
    // CPU time, so it can be compared against the moment the user last asked to record
    Moment At);
