namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Why a recorder engine didn't start recording. A withheld microphone and an audio
/// device that won't open need different advice, so they can't share one failure value.
/// </summary>
public enum RecorderStartResult
{
    Started = 0,
    NoPermission,
    NoDevice,
}
