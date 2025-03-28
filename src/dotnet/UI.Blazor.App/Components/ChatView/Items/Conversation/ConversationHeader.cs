namespace ActualChat.UI.Blazor.App.Components;

public sealed class ConversationHeader(Conversation conversation): ChatMessage(conversation.Id.StartEntryLid)
{
    public Conversation Conversation { get; } = conversation;

    public override bool Equals(ChatMessage? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;

        if (other is not ConversationHeader otherConversationHeader)
            return false;

        return Conversation.VersionEquals(otherConversationHeader.Conversation)
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
