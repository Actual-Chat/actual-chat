namespace ActualChat.UI.Blazor.App.Components;

public sealed class ThreadMessage(ChatEntry entry, Chat.Chat chat): ChatMessage(entry.Id.LocalId)
{
    public ChatEntry Entry { get; } = entry;
    public Chat.Chat Chat { get; } = chat;
    public ThreadChatId ChatId => (ThreadChatId)Chat.Id;

    public override bool Equals(ChatMessage? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;

        if (other is not ThreadMessage otherThreadMessage)
            return false;

        return Entry.VersionEquals(otherThreadMessage.Entry)
            && Kind == other.Kind
            && Date == other.Date
            && Flags == other.Flags;
    }

    public override int GetHashCode()
        => HashCode.Combine(Entry,
            Kind,
            Date,
            Flags);
}
