namespace ActualChat.UI.Blazor.App.Components;

public sealed class ConversationMessage : ChatMessage
{
    public ConversationMessage(Conversation conversation) : base(conversation.Id.StartEntryLid)
        => Conversation = conversation;

    // Set when EmitConversationCard also emitted a LiveConversationHeader for this card's conversation,
    // so the card must not render its own title - the header already owns it.
    public bool HasSplitHeader { get; init; }
    public override bool Equals(ChatMessage? other)
    {
        if (ReferenceEquals(null, other))
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (other is not ConversationMessage otherConversationMessage)
            return false;

        return Conversation!.VersionEquals(otherConversationMessage.Conversation)
            && Kind == other.Kind
            && Date == other.Date
            && Flags == other.Flags
            && HasSplitHeader == otherConversationMessage.HasSplitHeader;
    }

    public override int GetHashCode()
        => HashCode.Combine(Conversation,
            Kind,
            Date,
            Flags,
            HasSplitHeader);
}
