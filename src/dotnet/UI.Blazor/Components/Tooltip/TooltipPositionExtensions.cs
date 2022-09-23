namespace ActualChat.UI.Blazor.Components;

public static class TooltipPositionExtensions
{
    public static string ToStringPosition(this TooltipPosition position)
        => position switch {
            TooltipPosition.Top => "top",
            TooltipPosition.TopStart => "top-start",
            TooltipPosition.TopEnd => "top-end",
            TooltipPosition.Right => "right",
            TooltipPosition.RightStart => "right-start",
            TooltipPosition.RightEnd => "right-end",
            TooltipPosition.Bottom => "bottom",
            TooltipPosition.BottomStart => "bottom-start",
            TooltipPosition.BottomEnd => "bottom-end",
            TooltipPosition.Left => "left",
            TooltipPosition.LeftStart => "left-start",
            TooltipPosition.LeftEnd => "left-end",
            TooltipPosition.None => "",
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null)
        };
}
