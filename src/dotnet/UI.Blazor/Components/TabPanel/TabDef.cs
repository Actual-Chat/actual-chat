namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// One tab of a <see cref="TabPanel"/>. The panel takes the whole list as a parameter, so it knows
/// its tabs in the very render that draws their buttons - a tab that registers itself is one render
/// too late. List order is tab order.
/// </summary>
public sealed record TabDef(string Id, string Title = "")
{
    public RenderFragment? TitleContent { get; init; }
    public string Class { get; init; } = "";
    public RenderFragment? Content { get; init; }
}
