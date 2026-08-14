namespace ActualChat.UI.Blazor.Services;

// Append only: the value crosses to Android as an intent extra.
public enum ActivityKind
{
    Replaying,
    Listening,
    Recording,
    Armed,
    Uploading,
    SharingLocation,
    Downloading,
}
