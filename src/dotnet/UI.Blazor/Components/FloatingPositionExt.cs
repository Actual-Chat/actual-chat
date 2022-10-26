namespace ActualChat.UI.Blazor.Components;

public static class FloatingPositionExt
{
    public static string ToPositionString(this FloatingPosition position)
        => position switch {
            FloatingPosition.Top => "top",
            FloatingPosition.TopStart => "top-start",
            FloatingPosition.TopEnd => "top-end",
            FloatingPosition.Right => "right",
            FloatingPosition.RightStart => "right-start",
            FloatingPosition.RightEnd => "right-end",
            FloatingPosition.Bottom => "bottom",
            FloatingPosition.BottomStart => "bottom-start",
            FloatingPosition.BottomEnd => "bottom-end",
            FloatingPosition.Left => "left",
            FloatingPosition.LeftStart => "left-start",
            FloatingPosition.LeftEnd => "left-end",
            FloatingPosition.None => "",
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null)
        };
}
