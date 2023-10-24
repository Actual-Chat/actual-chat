using ActualChat.Media;

namespace ActualChat.Chat.UI.Blazor.Components;

[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class ChatMessage(ChatEntry entry) : IVirtualListItem, IEquatable<ChatMessage>
{
    private Symbol? _key;

    public Symbol Key => _key ??= GetKey();

    public ChatEntry Entry { get; } = entry;
    public ChatMessageReplacementKind ReplacementKind { get; init; }
    public DateOnly Date { get; init; }
    public ChatMessageFlags Flags { get; init; }
    public int CountAs { get; init; } = 1;

    public bool IsReplacement
        => ReplacementKind != ChatMessageReplacementKind.None;
    public bool ShowLinkPreview
        => Entry.LinkPreview is { IsEmpty: false } && Entry.LinkPreviewMode != LinkPreviewMode.None;

    public override string ToString()
        => $"(#{Key} -> {Entry})";

    private Symbol GetKey()
        => Entry.LocalId.Format() + ReplacementKind.GetKeySuffix();

    // Equality

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is ChatMessage other && Equals(other));

    public bool Equals(ChatMessage? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Entry.VersionEquals(other.Entry)
            && ReplacementKind == other.ReplacementKind
            && Date == other.Date
            && Flags == other.Flags
            && Entry.Attachments.SequenceEqual(other.Entry.Attachments)
            && Entry.LinkPreview == other.Entry.LinkPreview;
    }

    public override int GetHashCode()
        => HashCode.Combine(Entry, ReplacementKind, Date, Flags, Entry.Attachments.Count);

    public static bool operator ==(ChatMessage? left, ChatMessage? right) => Equals(left, right);
    public static bool operator !=(ChatMessage? left, ChatMessage? right) => !Equals(left, right);

    // Static helpers

    public static ChatMessage Welcome(ChatId chatId)
    {
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 0L, AssumeValid.Option);
        var chatEntry = new ChatEntry(chatEntryId);
        return new ChatMessage(chatEntry) { ReplacementKind = ChatMessageReplacementKind.WelcomeBlock };
    }
}
