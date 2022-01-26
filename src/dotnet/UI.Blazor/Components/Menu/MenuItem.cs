namespace ActualChat.UI.Blazor.Components;

public partial class MenuItem
{
    [Parameter]
    public string? Title { get; set; }
    [Parameter]
    public string? Icon { get; set; } = "";
    [Parameter]
    public bool IsTitleVisible { get; set; } = true;
    [Parameter]
    public bool IsIconVisible { get; set; }
    [Parameter]
    public string? CommonStyle { get; set; }
    [Parameter]
    public string? Name { get; set; }
}
