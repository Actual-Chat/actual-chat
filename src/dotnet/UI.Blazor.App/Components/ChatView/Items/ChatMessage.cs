namespace ActualChat.UI.Blazor.App.Components;

[ParameterComparer(typeof(ByValueParameterComparer))]
public abstract class ChatMessage(long id) : IVirtualListItem, IEquatable<ChatMessage>
{
    public ChatMessageKey Key => field ??= ChatMessageKey.New(Kind, Id);
    string IVirtualListItem.Key => Key.Value;
    public long Id { get; } = id;

    public bool ShouldSkipKey { get; init; }

    public ChatMessageKind Kind { get; init; }
    public DateOnly Date { get; init; }
    public ChatMessageFlags Flags { get; init; }
    public ChatMessage? PreviousMessage { get; init; }
    public ChatMessage? NextMessage { get; set; }
    public Conversation? Conversation { get; init; }
    public virtual bool IsGroup => false;

    public bool IsReplacement
        => Kind != ChatMessageKind.None;

    // Returns a shallow copy with NextMessage set. Used to assemble per-snapshot forward links
    // without mutating the shared, cached tile items (see ChatUI.GetTiles).
    public ChatMessage WithNextMessage(ChatMessage? next)
    {
        var clone = (ChatMessage)MemberwiseClone();
        clone.NextMessage = next;
        return clone;
    }

    public IEnumerable<ChatMessage> GetLeafMessages()
    {
        if (!IsGroup)
            yield return this;

        if (this is not IVirtualListGroup<ChatMessage> group)
            yield break;

        foreach (var child in group.Items)
        foreach (var leaf in child.GetLeafMessages())
            yield return leaf;
    }

    public override string ToString()
        => $"(#{Key})";

    // Equality

    public abstract bool Equals(ChatMessage? other);
    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is ChatMessage other && Equals(other));
    public abstract override int GetHashCode();

    public static bool operator ==(ChatMessage? left, ChatMessage? right) => Equals(left, right);
    public static bool operator !=(ChatMessage? left, ChatMessage? right) => !Equals(left, right);

    // Static helpers

    public static ChatMessage Welcome(ChatId chatId)
    {
        var chatEntry = new TextEntry(ChatEntryId.New(chatId, 0L));
        return new ChatEntryMessage(chatEntry) { Kind = ChatMessageKind.WelcomeBlock };
    }
}
