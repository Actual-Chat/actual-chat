namespace ActualChat.UI.Blazor.Components;

public enum DialogFramePosition
{
    Default = 0, // Default
    Bottom,
}

public sealed record DialogFrameNarrowViewSettings
{
    public static readonly DialogFrameNarrowViewSettings Default = new ();
    public static readonly DialogFrameNarrowViewSettings Bottom = new () { Position = DialogFramePosition.Bottom };

    public DialogFramePosition Position { get; init; }
    public bool? ShouldHideButtons { get; init; }
}
