namespace ActualChat.Chat.UI.Blazor.Components;

// EntryLid = Entry's LocalId
public sealed record ChatViewItemVisibility(
    IReadOnlySet<long> VisibleEntryLids,
    bool IsEndAnchorVisible)
{
    public static ChatViewItemVisibility Empty { get; } = new(ImmutableHashSet<long>.Empty, true);

    // EntryLid = Entry's LocalId
    public long MinEntryLid { get; } = VisibleEntryLids.Count == 0 ? -1 : VisibleEntryLids.Min();
    public long MaxEntryLid { get; } = VisibleEntryLids.Count == 0 ? -1 : VisibleEntryLids.Max();
    public bool IsEmpty => VisibleEntryLids.Count == 0;

    public ChatViewItemVisibility(VirtualListItemVisibility source)
        : this(
            source.VisibleKeys.Select(k => long.Parse(k, CultureInfo.InvariantCulture)).ToHashSet(),
            source.IsEndAnchorVisible)
    { }

    public bool IsPartiallyVisible(long entryLid)
        => !IsEmpty && (entryLid == MinEntryLid || entryLid == MaxEntryLid);

    public bool IsFullyVisible(long entryLid)
        => VisibleEntryLids.Contains(entryLid) && !IsPartiallyVisible(entryLid);

    public bool ContentEquals(ChatViewItemVisibility other)
    {
        if (VisibleEntryLids.Count != other.VisibleEntryLids.Count)
            return false;
        if (IsEndAnchorVisible != other.IsEndAnchorVisible)
            return false;

        foreach (var entryLid in other.VisibleEntryLids)
            if (!VisibleEntryLids.Contains(entryLid))
                return false;

        return true;
    }
}
