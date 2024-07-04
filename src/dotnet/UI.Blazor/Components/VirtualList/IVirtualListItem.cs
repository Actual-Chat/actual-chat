namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListItem
{
    string Key { get; }
    string RenderKey => Key;
    int CountAs { get; }
}
