namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListItem
{
    Symbol Key { get; }
    int CountAs { get; }
    bool IsFirstTimeRendered { get; }
}
