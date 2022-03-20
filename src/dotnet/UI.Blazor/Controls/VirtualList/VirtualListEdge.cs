namespace ActualChat.UI.Blazor.Controls;

public enum VirtualListEdge
{
    Start = 0,
    End = 1,
}

public static class VirtualListEdgeExt
{
    public static bool IsStart(this VirtualListEdge edge)
        => edge == VirtualListEdge.Start;

    public static bool IsEnd(this VirtualListEdge edge)
        => edge == VirtualListEdge.End;
}
