namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListItem
{
    string Key { get; }
    string RenderKey => Key;
    bool IsGroup { get; }
    bool ShouldSkipKey { get; }
}
