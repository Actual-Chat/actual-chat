namespace ActualChat.UI.Blazor.Components;

public record TooltipOptions(
    TooltipPosition Position = TooltipPosition.Top)
{
    public static TooltipOptions Default = new();
}
