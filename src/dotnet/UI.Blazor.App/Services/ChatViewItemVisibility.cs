namespace ActualChat.UI.Blazor.App.Services;

// EntryLid = Entry's LocalId
public sealed record ChatViewItemVisibility(
    ChatId ChatId,
    IReadOnlySet<ChatMessageKey> VisibleKeys,
    bool IsEndAnchorVisible)
{
    public static readonly ChatViewItemVisibility Empty = new(null!, ImmutableHashSet<ChatMessageKey>.Empty, false);

    public long MinMessageLid { get; } = VisibleKeys.Count == 0 ? -1 : VisibleKeys.Min(x => x.LocalId);
    public long MaxMessageLid { get; } = VisibleKeys.Count == 0 ? -1 : VisibleKeys.Max(x => x.LocalId);
    public bool IsEmpty => VisibleKeys.Count == 0;

    [field: AllowNull, MaybeNull]
    public IReadOnlyList<TextEntryId> VisibleTextEntryIds => field ??= VisibleKeys
        .Where(c => c.Kind == ChatMessageKind.None)
        .OrderBy(x => x.LocalId)
        .Select(x => TextEntryId.New(ChatId, x.LocalId))
        .ToImmutableArray();
    [field:AllowNull, MaybeNull]
    public IReadOnlySet<long> VisibleMessageLids => field ??= VisibleKeys.Select(x => x.LocalId).ToHashSet();

    public ChatViewItemVisibility(VirtualListItemVisibility source)
        : this(
            ChatId.Parse(source.ListIdentity),
            source.VisibleKeys
                .Select(ChatMessageKey.Parse)
                .ToHashSet(),
            source.IsEndAnchorVisible)
    { }

    public bool IsScrollRequired(long entryLid)
    {
        var found = VisibleKeys.Any(x => x.LocalId == entryLid && x.Kind == ChatMessageKind.None);
        if (!found)
            return true;

        var hasBefore = VisibleKeys.Any(x => x.LocalId < entryLid || (x.LocalId == entryLid && x.Kind != ChatMessageKind.None));
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

        foreach (var key in other.VisibleKeys)
            if (!VisibleKeys.Contains(key))
                return false;

        return true;
    }

    // This record relies on referential equality
    public bool Equals(ChatViewItemVisibility? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
