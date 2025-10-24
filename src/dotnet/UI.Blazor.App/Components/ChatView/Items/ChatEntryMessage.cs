namespace ActualChat.UI.Blazor.App.Components;

public sealed class ChatEntryMessage(ChatEntry entry): ChatMessage(entry.Id.LocalId)
{
    public ChatEntry Entry { get; } = entry;

    public override bool Equals(ChatMessage? other)
    {
        if (ReferenceEquals(null, other))
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (other is not ChatEntryMessage otherEntryMessage)
            return false;

        // Avoid checking the version for entry as we don't want to rerender a virtual list
        // do not check IsStreaming - it will lead to rerendering the message, and the message is able to rerender itself
        return Entry.Id == otherEntryMessage.Entry.Id
            && Entry.IsSending == otherEntryMessage.Entry.IsSending
            && Entry.IsRemoved == otherEntryMessage.Entry.IsRemoved
            && Entry.HasReactions == otherEntryMessage.Entry.HasReactions
            && OrdinalEquals(Entry.Content, otherEntryMessage.Entry.Content)
            && Kind == other.Kind
            && Date == other.Date
            && Flags == other.Flags
            && Entry.Attachments.SequenceEqual(otherEntryMessage.Entry.Attachments)
            && Entry.LinkPreviews.SequenceEqual(otherEntryMessage.Entry.LinkPreviews);
    }

    public override int GetHashCode()
        => HashCode.Combine(Entry.Id,
            Kind,
            Date,
            Flags,
            Entry.Attachments.Length);
}
