namespace ActualChat.UI.Blazor.App.Services;

// EntryLid = Entry's LocalId
public sealed record ChatViewItemVisibility(
    ChatId ChatId,
    IReadOnlySet<long> VisibleEntryLids,
    bool IsEndAnchorVisible)
{
    public static readonly ChatViewItemVisibility Empty = new(null!, ImmutableHashSet<long>.Empty, false);

    // EntryLid = Entry's LocalId
    public long MinEntryLid { get; } = VisibleEntryLids.Count == 0 ? -1 : VisibleEntryLids.Min();
    public long MaxEntryLid { get; } = VisibleEntryLids.Count == 0 ? -1 : VisibleEntryLids.Max();
    public bool IsEmpty => VisibleEntryLids.Count == 0;
    public IEnumerable<TextEntryId> VisibleEntryIds => VisibleEntryLids.Select(lid => TextEntryId.New(ChatId, lid));

    public ChatViewItemVisibility(VirtualListItemVisibility source)
        : this(
            ChatId.Parse(source.ListIdentity),
            source.VisibleKeys
                .Select(k => k.Split('-')[0])
                .Select(k =>NumberExt.TryParseLong(k, out var lid) ? lid : 0)
                .Where(lid => lid > 0)
                .ToHashSet(),
            source.IsEndAnchorVisible)
    { }

    public bool IsPartiallyVisible(long entryLid)
        => !IsEmpty && (entryLid == MinEntryLid || entryLid == MaxEntryLid);

    public bool IsFullyVisible(long entryLid)
        => VisibleEntryLids.Contains(entryLid) && !IsPartiallyVisible(entryLid);

    public bool IsIdenticalTo(ChatViewItemVisibility other)
    {
        if (ChatId != other.ChatId)
            return false;

        if (VisibleEntryLids.Count != other.VisibleEntryLids.Count)
            return false;

        if (IsEndAnchorVisible != other.IsEndAnchorVisible)
            return false;

        foreach (var entryLid in other.VisibleEntryLids)
            if (!VisibleEntryLids.Contains(entryLid))
                return false;

        return true;
    }

    // This record relies on referential equality
    public bool Equals(ChatViewItemVisibility? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
