using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatState
{
    public const int MaxUnreadMessageCount = 1000;
    public static ChatState None { get; } = new() { Chat = Chat.None, Summary = ChatSummary.None };
    public static ChatState Loading { get; } = new() { Chat = Chat.Loading, Summary = ChatSummary.None };

    private bool? _hasMentions;
    private Trimmed<int>? _unreadMessageCount;

    public Chat Chat { get; init; } = null!;
    public ChatSummary Summary { get; init; } = null!;
    public Contact? Contact { get; init; }
    public Presence Presence { get; init; } = Presence.Unknown;
    public Mention? LastMention { get; init; }
    public long? ReadEntryId { get; init; }
    public bool IsSelected { get; init; }
    public bool IsPinned { get; init; }
    public bool IsListening { get; init; }
    public bool IsRecording { get; init; }

    // Computed

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

            var lastId = Summary.TextEntryIdRange.End - 1;
            var count = (lastId - readEntryId).Clamp(0, MaxUnreadMessageCount);
            _unreadMessageCount = unreadMessageCount = new Trimmed<int>((int)count, MaxUnreadMessageCount);
            return unreadMessageCount;
        }
    }

    public bool HasMentionsOrUnreadMessages => HasMentions || UnreadMessageCount.Value > 0;

    // This record relies on referential equality
    public virtual bool Equals(ChatState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
