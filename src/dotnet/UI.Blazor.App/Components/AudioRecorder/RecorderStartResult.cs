namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Why a recorder engine didn't start recording. Each value maps to advice the user can act on,
/// so a withheld microphone, a missing one and one another app holds can't share a value.
/// </summary>
public enum RecorderStartResult
{
    Started = 0,
    NoPermission,
    NoDevice,
    DeviceBusy,
    Unknown,
}
