namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Debug hooks into the audio UI. Lives here rather than in the app layer so
/// <see cref="DebugUI"/> can reach them without referencing it.
/// </summary>
public interface IDebugAudio
{
    bool IsAudioSyncEnabled { get; set; }
    // "" or null clears the override; otherwise "<RecordingStatusKind>" or "<RecordingStatusKind>:<code>"
    void ForceRecordingStatus(string? status);
}
