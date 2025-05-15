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
        return Entry.Id == otherEntryMessage.Entry.Id
            && Entry.IsRemoved == otherEntryMessage.Entry.IsRemoved
            && Entry.HasReactions == otherEntryMessage.Entry.HasReactions
            && Entry.IsStreaming == otherEntryMessage.Entry.IsStreaming
            && ReplacementKind == other.ReplacementKind
            && Date == other.Date
            && Flags == other.Flags
            && Entry.Attachments.SequenceEqual(otherEntryMessage.Entry.Attachments)
            && Entry.LinkPreviews.SequenceEqual(otherEntryMessage.Entry.LinkPreviews);
    }

    public override int GetHashCode()
        => HashCode.Combine(Entry.Id,
            ReplacementKind,
            Date,
            Flags,
            Entry.Attachments.Length);
}
