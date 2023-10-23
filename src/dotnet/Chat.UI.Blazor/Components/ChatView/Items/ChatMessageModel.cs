using ActualChat.Media;

namespace ActualChat.Chat.UI.Blazor.Components;

[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class ChatMessageModel(ChatEntry entry) : IVirtualListItem, IEquatable<ChatMessageModel>
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
        => ReferenceEquals(this, obj) || (obj is ChatMessageModel other && Equals(other));

    public bool Equals(ChatMessageModel? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Entry == other.Entry
            && ReplacementKind == other.ReplacementKind
            && Date == other.Date
            && Flags == other.Flags
            && Entry.Attachments.SequenceEqual(other.Entry.Attachments);
    }

    public override int GetHashCode()
        => HashCode.Combine(Entry, ReplacementKind, Date, Flags, Entry.Attachments.Count);

    public static bool operator ==(ChatMessageModel? left, ChatMessageModel? right) => Equals(left, right);
    public static bool operator !=(ChatMessageModel? left, ChatMessageModel? right) => !Equals(left, right);

    // Static helpers

    public static ChatMessageModel Welcome(ChatId chatId)
    {
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 0L, AssumeValid.Option);
        var chatEntry = new ChatEntry(chatEntryId);
        return new ChatMessageModel(chatEntry) { ReplacementKind = ChatMessageReplacementKind.WelcomeBlock };
    }
}
