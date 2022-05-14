namespace ActualChat.UI.Blazor.Components;

public interface IMenu
{
    string ItemClass { get; }
    MenuOrientation Orientation { get; }
    MenuIconPosition IconPosition { get; }
    MenuTooltipPosition TooltipPosition { get; }

    Task OnItemClick(MenuItem item);
}
