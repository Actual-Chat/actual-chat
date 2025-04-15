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

        return Entry.VersionEquals(otherEntryMessage.Entry)
            && ReplacementKind == other.ReplacementKind
            && Date == other.Date
            && Flags == other.Flags
            && Entry.Attachments.SequenceEqual(otherEntryMessage.Entry.Attachments)
            && Entry.LinkPreviews.SequenceEqual(otherEntryMessage.Entry.LinkPreviews);
    }

    public override int GetHashCode()
        => HashCode.Combine(Entry,
            ReplacementKind,
            Date,
            Flags,
            Entry.Attachments.Length);
}
