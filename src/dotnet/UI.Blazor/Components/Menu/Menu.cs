namespace ActualChat.UI.Blazor.Components;

public partial class Menu
{
    [Parameter]
    public bool IsOpen { get; set; }

    [Parameter]
    public string MenuStyle { get; set; } = "";
    [Parameter]
    public string Title { get; set; } = "";
    [Parameter]
    public string Icon { get; set; } = "";
    [Parameter]
    public bool IsHorizontal { get; set; }
    [Parameter]
    public string Name { get; set; } = "";
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private bool ItemsTitleVisible { get; set; } = true;
    private bool ItemsIconVisible { get; set; }
}
