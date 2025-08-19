namespace ActualChat.UI.Blazor.App.Components;

public class ChatEntryAuthorGroup : ChatMessage, IVirtualListGroup<ChatEntryMessage>
{
    public ChatEntryAuthorGroup(AuthorId authorId, IReadOnlyList<ChatEntryMessage> items)
        : base(items[0].Id)
    {
        AuthorId = authorId;
        Items = items;
        Kind = ChatMessageKind.Group;
    }

    public override bool IsGroup => true;
    public AuthorId AuthorId { get; }
    public IReadOnlyList<ChatEntryMessage> Items { get; }

    public override bool Equals(ChatMessage? other)
    {
        if (ReferenceEquals(null, other))
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (other is not ChatEntryAuthorGroup chatEntryAuthorGroup)
            return false;

        return Key.Equals(chatEntryAuthorGroup.Key)
            && Kind == other.Kind
            && Date == other.Date
            && Flags == other.Flags
            && Items.Count == chatEntryAuthorGroup.Items.Count
            && Items.SequenceEqual(chatEntryAuthorGroup.Items);
    }

    public override int GetHashCode()
        => Key.GetHashCode();
}
