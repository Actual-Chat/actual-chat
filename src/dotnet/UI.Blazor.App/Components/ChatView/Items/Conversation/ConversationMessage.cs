namespace ActualChat.UI.Blazor.App.Components;

public sealed class ConversationMessage : ChatMessage
{
    public ConversationMessage(Conversation conversation) : base(conversation.Id.StartEntryLid)
        => Conversation = conversation;

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
            && Flags == other.Flags;
    }

    public override int GetHashCode()
        => HashCode.Combine(Conversation,
            Kind,
            Date,
            Flags);
}
