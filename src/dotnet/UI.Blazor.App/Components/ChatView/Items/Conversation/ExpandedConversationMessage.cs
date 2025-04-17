namespace ActualChat.UI.Blazor.App.Components;

public sealed class ExpandedConversationMessage : ChatMessage, IVirtualListGroup<ChatMessage>
{
    public ExpandedConversationMessage(Conversation conversation, IReadOnlyList<ChatMessage> items)
        : base(conversation.Id.StartEntryLid)
    {
        Conversation = conversation;
        Items = items;
        ReplacementKind = ChatMessageReplacementKind.ConversationBlock;
    }

    public override bool IsGroup => true;
    public IReadOnlyList<ChatMessage> Items { get; }

    public override bool Equals(ChatMessage? other)
    {
        if (ReferenceEquals(null, other))
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (other is not ExpandedConversationMessage otherConversationMessage)
            return false;

        return Key.Equals(otherConversationMessage.Key)
            && Conversation!.VersionEquals(otherConversationMessage.Conversation)
            && Items.Count == otherConversationMessage.Items.Count
            && Items.SequenceEqual(otherConversationMessage.Items);
    }

    public override int GetHashCode()
        => HashCode.Combine(Conversation, Key);
}
