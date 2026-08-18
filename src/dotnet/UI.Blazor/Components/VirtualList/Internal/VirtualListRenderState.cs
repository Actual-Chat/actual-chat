namespace ActualChat.UI.Blazor.Components.Internal;

/// <summary> The data transferred from Blazor to JS. </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class VirtualListRenderState
{
    public long RenderIndex { get; init; }
    public VirtualListDataQuery Query { get; init; } = VirtualListDataQuery.None;

    public Range<string> KeyRange { get; init; }
    public int? BeforeCount { get; init; }
    public int? AfterCount { get; init; }
    // Indexes of items followed by a block separator, across the whole list
    public IReadOnlyList<int>? SeparatorIndexes { get; init; }
    public int Count { get; init; }
    public int? EstimatedCount { get; init; }

    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }

    public string? ScrollToKey { get; init; }
    public bool? ScrollToKeyInTheMiddle { get; init; }
}
