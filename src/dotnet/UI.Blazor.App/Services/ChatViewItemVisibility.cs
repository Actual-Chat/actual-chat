namespace ActualChat.UI.Blazor.App.Services;

// EntryLid = Entry's LocalId
public sealed record ChatViewItemVisibility(
    ChatId ChatId,
    IReadOnlySet<ChatMessageKey> VisibleKeys,
    bool IsEndAnchorVisible,
    bool IsPinnedToEnd)
{
    public static readonly ChatViewItemVisibility Empty
        = new(null!, ImmutableHashSet<ChatMessageKey>.Empty, false, false);

    public long MinMessageLid { get; } = VisibleKeys.Count == 0 ? -1 : VisibleKeys.Min(x => x.LocalId);
    public long MaxMessageLid { get; } = VisibleKeys.Count == 0 ? -1 : VisibleKeys.Max(x => x.LocalId);
    public bool IsEmpty => VisibleKeys.Count == 0;

    // Same as MaxMessageLid, minus the placeholders - use this one wherever the lid is about to
    // become a read position, since a placeholder's synthetic lid would mark the chat read to its end.
    public long MaxEntryLid { get; } = VisibleKeys
        .Where(x => !x.Kind.IsPlaceholder())
        .Select(x => x.LocalId)
        .DefaultIfEmpty(-1)
        .Max();

    public IReadOnlyList<ChatEntryId> VisibleTextEntryIds => field ??= VisibleKeys
        .Where(c => c.Kind == ChatMessageKind.None)
        .OrderBy(x => x.LocalId)
        .Select(x => ChatEntryId.New(ChatId, x.LocalId))
        .ToImmutableArray();
    public IReadOnlySet<long> VisibleMessageLids => field ??= VisibleKeys.Select(x => x.LocalId).ToHashSet();

    public ChatViewItemVisibility(VirtualListItemVisibility source)
        : this(
            ChatId.Parse(source.ListIdentity),
            source.VisibleKeys
                .Select(ChatMessageKey.Parse)
                .ToHashSet(),
            source.IsEndAnchorVisible,
            source.IsPinnedToEnd)
    { }

    public bool IsScrollRequired(long entryLid)
    {
        var found = VisibleKeys.Any(x => x.LocalId == entryLid && x.Kind == ChatMessageKind.None);
        if (!found)
            return true;

        var hasBefore = VisibleKeys.Any(x => x.LocalId < entryLid
            || (x.LocalId == entryLid && x.Kind != ChatMessageKind.None));
        var hasAfter = VisibleKeys.Any(x => x.LocalId > entryLid);

        if (!hasBefore && !hasAfter) // the only visible item
            return false;
        if (hasBefore && hasAfter)
            return false;
        if (!hasAfter && IsEndAnchorVisible) // the last item
            return false;

        return true;
    }

    public bool IsIdenticalTo(ChatViewItemVisibility other)
    {
        if (ChatId != other.ChatId)
            return false;
        if (VisibleKeys.Count != other.VisibleKeys.Count)
            return false;
        if (IsEndAnchorVisible != other.IsEndAnchorVisible)
            return false;
        if (IsPinnedToEnd != other.IsPinnedToEnd)
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
