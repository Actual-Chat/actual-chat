namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListItem
{
    string Key { get; }
    string RenderKey => Key;
    bool IsGroup { get; }
    bool ShouldSkipKey { get; }
    // Read by FiniteList only: an item carrying extra height (a block separator) is a bad
    // item-size sample, so it's excluded from measurement.
    bool HasRegularSize => true;
}
