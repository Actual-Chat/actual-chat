namespace ActualChat.UI.Blazor.Components.Internal;

public class VirtualListRenderItem
{
    public int CountAs { get; set; }
    public int DataHash { get; set; }

    public VirtualListRenderItem(IVirtualListItem item)
    {
        CountAs = item.CountAs;
        DataHash = item.GetHashCode();
    }
}
