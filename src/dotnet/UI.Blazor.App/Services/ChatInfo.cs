using ActualChat.Contacts;
using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record ChatInfo(Contact Contact) : IHasId<ChatId>
{
    public const int MaxUnreadCount = 1000;
    public const int MaxLastTextEntryContentLength = 100;

    public ChatUserSettings ChatUserSettings { get; init; } = ChatUserSettings.Default;
    public ChatNews? News { get; init; }
    public Mention? LastMention { get; init; }
    public long ReadEntryLid { get; init; }
    public Trimmed<int> UnreadCount { get; init; }
    public bool HasUnreadMentions { get; init; }
    public bool IsPinnedToNavbar { get; init; }

    // Shortcuts
    public ChatId Id => Contact.Id.ChatId;
    public Chat.Chat Chat => Contact.Chat;
    public ChatEntry? LastTextEntry => News?.LastTextEntry;

    // Computed
    public bool HasUnreadOwnMention => LastMention is { } m && m.EntryId.LocalId > ReadEntryLid;
    public SearchDocument SearchDocument => field = field.OrNew(Chat.Title);
    public Trimmed<int> UnmutedUnreadCount => ChatUserSettings.NotificationMode switch {
        ChatNotificationMode.ImportantOnly => (HasUnreadMentions ? 1 : 0, MaxUnreadCount),
        ChatNotificationMode.Muted => (0, MaxUnreadCount),
        _ => UnreadCount != 0 ? UnreadCount : (HasUnreadMentions ? 1 : 0, MaxUnreadCount),
    };

    // This record relies on referential equality
    public bool Equals(ChatInfo? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
