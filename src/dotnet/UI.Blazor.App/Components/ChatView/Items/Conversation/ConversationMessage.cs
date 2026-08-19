namespace ActualChat.UI.Blazor.App.Components;

public sealed class ConversationMessage : ChatMessage
{
    public ConversationMessage(Conversation conversation) : base(conversation.Id.StartEntryLid)
        => Conversation = conversation;

    public override bool MustAnimateAppearance
        // The card is one item in both forms of the block, so it never really arrives.
        => false;

    // Set when EmitConversationCard also emitted a LiveConversationHeader for this card's conversation,
    // so the card must not render its own title - the header already owns it.
    public bool HasSplitHeader { get; init; }
    // Set when this card is the live block's card: the block appends its own trailing ConversationFooter,
    // so the card must not render its own - otherwise a materialized block shows the footer twice.
    public bool HasSplitFooter { get; init; }

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
            && HasSplitHeader == otherConversationMessage.HasSplitHeader
            && HasSplitFooter == otherConversationMessage.HasSplitFooter;
    }

    public override int GetHashCode()
        => HashCode.Combine(Conversation,
            Kind,
            Date,
            Flags,
            HasSplitHeader,
            HasSplitFooter);
}
