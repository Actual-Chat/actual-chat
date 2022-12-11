using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record ChatInfo(Contact Contact) : IHasId<ChatId>
{
    public const int MaxUnreadCount = 1000;
    public static ChatInfo None { get; } = new(Contact.None);
    public static ChatInfo Loading { get; } = new(Contact.Loading);

    private bool? _hasMentions;
    private Trimmed<int>? _unreadMessageCount;

    public ChatNews News { get; init; }
    public Mention? LastMention { get; init; }
    public long? ReadEntryId { get; init; }
    public UserChatSettings UserSettings { get; init; } = UserChatSettings.Default;

    // Shortcuts
    public ChatId Id => Contact.Id.ChatId;
    public Chat Chat => Contact.Chat;

    public Trimmed<int> UnmutedUnreadCount => UserSettings.NotificationMode switch {
        ChatNotificationMode.ImportantOnly => (HasUnreadMentions ? 1 : 0, MaxUnreadCount),
        ChatNotificationMode.Muted => (0, MaxUnreadCount),
        _ => UnreadCount != 0 ? UnreadCount : (HasUnreadMentions ? 1 : 0, MaxUnreadCount),
    };

    public bool HasUnreadMentions {
        get {
            if (_hasMentions is { } hasMentions)
                return hasMentions;

            hasMentions = false;
            if (UserSettings.NotificationMode is not ChatNotificationMode.Muted) {
                var readEntryId = ReadEntryId ?? 0;
                var lastMentionEntryId = LastMention?.EntryId.LocalId ?? 0;
                hasMentions = lastMentionEntryId > readEntryId;
            }
            _hasMentions = hasMentions;
            return hasMentions;
        }
    }

    public Trimmed<int> UnreadCount {
        get {
            if (_unreadMessageCount is { } unreadMessageCount)
                return unreadMessageCount;

            unreadMessageCount = (0, MaxUnreadCount);
            if (ReadEntryId is { } readEntryId) { // Otherwise the chat wasn't ever opened
                var lastId = News.TextEntryIdRange.End - 1;
                var count = (lastId - readEntryId).Clamp(0, MaxUnreadCount);
                unreadMessageCount = new Trimmed<int>((int)count, MaxUnreadCount);
            }
            _unreadMessageCount = unreadMessageCount;
            return unreadMessageCount;
        }
    }

    // This record relies on referential equality
    public bool Equals(ChatInfo? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
