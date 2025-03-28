namespace ActualChat.UI.Blazor.App.Components;

public sealed class ThreadMessage(ChatEntry entry): ChatMessage(entry.Id.LocalId)
{
    public ChatEntry Entry { get; } = entry;

    public override bool Equals(ChatMessage? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;

        if (other is not ThreadMessage otherThreadMessage)
            return false;

        return Entry.VersionEquals(otherThreadMessage.Entry)
            && ReplacementKind == other.ReplacementKind
            && Date == other.Date
            && Flags == other.Flags;
    }

    public override int GetHashCode()
        => HashCode.Combine(Entry,
            ReplacementKind,
            Date,
            Flags);
}
