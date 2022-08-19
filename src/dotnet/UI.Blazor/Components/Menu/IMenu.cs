namespace ActualChat.UI.Blazor.Components;

public interface IMenu
{
    string ItemClass { get; }
    MenuTooltipPosition TooltipPosition { get; }

    Task OnItemClick(MenuItem item);
}
