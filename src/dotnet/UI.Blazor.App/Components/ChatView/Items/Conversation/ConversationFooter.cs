namespace ActualChat.UI.Blazor.App.Components;

public sealed class ConversationFooter : ChatMessage
{
    public ConversationFooter(Conversation conversation) : base(conversation.EndEntryLid)
        => Conversation = conversation;

    public override bool Equals(ChatMessage? other)
    {
        if (ReferenceEquals(null, other))
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (other is not ConversationFooter otherConversationFooter)
            return false;

        return Conversation!.VersionEquals(otherConversationFooter.Conversation)
            && ReplacementKind == other.ReplacementKind
            && Date == other.Date
            && Flags == other.Flags;
    }

    public override int GetHashCode()
        => HashCode.Combine(Conversation,
            ReplacementKind,
            Date,
            Flags);
}
