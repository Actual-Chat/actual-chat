namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// One tab of a <see cref="SettingsPanel"/>. The panel takes the whole list as a parameter, so it
/// knows its tabs in the very render that draws their buttons. List order is tab order.
/// </summary>
public sealed record SettingsTabDef(string Id, string Title)
{
    public string IconClass { get; init; } = "";
    public bool HasSeparatorBefore { get; init; }
    public string Class { get; init; } = "";
    public string HeaderClass { get; init; } = "";
    public string ContentClass { get; init; } = "";
    public RenderFragment? TitleContent { get; init; }
    public RenderFragment? Content { get; init; }
    // An action tab - Log out, Quit - runs this instead of becoming the selection
    public Action? Click { get; init; }
}
