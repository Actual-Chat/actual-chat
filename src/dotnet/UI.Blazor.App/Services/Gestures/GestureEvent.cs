namespace ActualChat.UI.Blazor.App.Services.Gestures;

[StructLayout(LayoutKind.Auto)]
public readonly record struct GestureEvent(GestureKind Kind, Moment At);

public enum GestureKind
{
    None = 0,
    FlipToTalk,
    DoubleShake,
    FaceDown,
}
