using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record ChatInfo(Contact Contact) : IHasId<ChatId>
{
    public const int MaxUnreadCount = 1000;
    public const int MaxLastTextEntryContentLength = 100;

    public static ChatInfo None { get; } = new(SpecialContact.Unavailable);
    public static ChatInfo Loading { get; } = new(SpecialContact.Loading);

    public ChatNews News { get; init; }
    public UserChatSettings UserSettings { get; init; } = UserChatSettings.Default;
    public Mention? LastMention { get; init; }
    public long ReadEntryLid { get; init; }
    public Trimmed<int> UnreadCount { get; init; }
    public bool HasUnreadMentions { get; init; }
    public string LastTextEntryText { get; init; } = "";

    // Shortcuts
    public ChatId Id => Contact.Id.ChatId;
    public Chat Chat => Contact.Chat;
    public ChatEntry? LastTextEntry => News.LastTextEntry;

    // Computed
    public Trimmed<int> UnmutedUnreadCount => UserSettings.NotificationMode switch {
        ChatNotificationMode.ImportantOnly => (HasUnreadMentions ? 1 : 0, MaxUnreadCount),
        ChatNotificationMode.Muted => (0, MaxUnreadCount),
        _ => UnreadCount != 0 ? UnreadCount : (HasUnreadMentions ? 1 : 0, MaxUnreadCount),
    };

    // This record relies on referential equality
    public bool Equals(ChatInfo? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
