namespace ActualChat.UI.Blazor.App.Services;

// EntryLid = Entry's LocalId
public sealed record ChatViewItemVisibility(
    ChatId ChatId,
    IReadOnlySet<ChatMessageKey> VisibleKeys,
    bool IsEndAnchorVisible)
{
    public static readonly ChatViewItemVisibility Empty = new(null!, ImmutableHashSet<ChatMessageKey>.Empty, false);

    // EntryLid = Entry's LocalId
    public long MinEntryLid { get; } = VisibleKeys.Count == 0 ? -1 : VisibleKeys.Min(x => x.LocalId);
    public long MaxEntryLid { get; } = VisibleKeys.Count == 0 ? -1 : VisibleKeys.Max(x => x.LocalId);
    public bool IsEmpty => VisibleKeys.Count == 0;
    public IEnumerable<TextEntryId> VisibleEntryIds => VisibleKeys.Select(x => TextEntryId.New(ChatId, x.LocalId));
    public IReadOnlySet<long> VisibleEntryLids => VisibleKeys.Select(x => x.LocalId).ToHashSet();

    public ChatViewItemVisibility(VirtualListItemVisibility source)
        : this(
            ChatId.Parse(source.ListIdentity),
            source.VisibleKeys
                .Select(ChatMessageKey.Parse)
                .Where(x => x.LocalId > 0)
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

        foreach (var key in other.VisibleKeys)
            if (!VisibleKeys.Contains(key))
                return false;

        return true;
    }

    // This record relies on referential equality
    public bool Equals(ChatViewItemVisibility? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
