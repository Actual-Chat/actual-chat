using ActualChat.Contacts;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed record ChatSummary(Contact Contact)
{
    public const int MaxUnreadMessageCount = 1000;
    public static ChatSummary None { get; } = new(Contact.None);
    public static ChatSummary Loading { get; } = new(Contact.Loading);

    private bool? _hasMentions;
    private Trimmed<int>? _unreadMessageCount;

    public ChatNews News { get; init; } = ChatNews.None;
    public Mention? LastMention { get; init; }
    public long? ReadEntryId { get; init; }

    // Computed
    public Chat Chat => Contact.Chat;

    public bool HasMentions {
        get {
            if (_hasMentions is { } hasMentions)
                return hasMentions;

            var readEntryId = ReadEntryId ?? 0;
            var lastMentionEntryId = LastMention?.EntryId.LocalId ?? 0;
            _hasMentions = hasMentions = lastMentionEntryId > readEntryId;
            return hasMentions;
        }
    }

    public Trimmed<int> UnreadMessageCount {
        get {
            if (_unreadMessageCount is { } unreadMessageCount)
                return unreadMessageCount;

            if (ReadEntryId is not { } readEntryId)
                return (0, MaxUnreadMessageCount); // Never opened this chat, so no unread messages

            var lastId = News.TextEntryIdRange.End - 1;
            var count = (lastId - readEntryId).Clamp(0, MaxUnreadMessageCount);
            _unreadMessageCount = unreadMessageCount = new Trimmed<int>((int)count, MaxUnreadMessageCount);
            return unreadMessageCount;
        }
    }

    public bool HasMentionsOrUnreadMessages
        => HasMentions || UnreadMessageCount.Value > 0;

    // This record relies on referential equality
    public bool Equals(ChatSummary? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
