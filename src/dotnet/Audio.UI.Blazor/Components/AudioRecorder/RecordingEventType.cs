namespace ActualChat.Audio.UI.Blazor.Components;

public enum RecordingEventType : byte
{
    Data = 1,
    Pause,
    Resume,
    Voice,
    Timestamp,
}
