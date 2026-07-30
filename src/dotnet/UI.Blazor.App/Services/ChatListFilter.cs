namespace ActualChat.UI.Blazor.App.Services;

public sealed record ChatListFilter(
    Symbol Id,
    string Title,
    Func<Chat.Chat, bool>? Filter = null,
    bool AcrossPlace = false,
    Func<ChatInfo, bool>? ChatInfoFilter = null
) {
    public static readonly ChatListFilter None = new("", "All", _ => true);
    public static readonly ChatListFilter People = new("@people", "People", c => c.Kind == ChatKind.Peer);
    public static readonly ChatListFilter Groups = new("@groups", "Groups", c => c.Kind != ChatKind.Peer);
    public static readonly ChatListFilter Unread = new("@unread", "Unread",
        AcrossPlace: true, ChatInfoFilter: c => c.UnmutedUnreadCount > 0);
    public static readonly ChatListFilter UnreadDMs = new("@unread-dms", "DMs",
        c => c.Kind == ChatKind.Peer, AcrossPlace: true, ChatInfoFilter: c => c.UnmutedUnreadCount > 0);
    public static readonly ChatListFilter UnreadMentions = new("@unread-mentions", "Mentions",
        AcrossPlace: true, ChatInfoFilter: c => c.HasUnreadOwnMention);
    public static readonly ChatListFilter[] All = [None, Groups, People];
    private static readonly ChatListFilter[] AllKnown = [None, Groups, People, Unread, UnreadDMs, UnreadMentions];

    public bool Invoke(ChatInfo chatInfo)
        => (Filter?.Invoke(chatInfo.Chat) ?? true) && (ChatInfoFilter?.Invoke(chatInfo) ?? true);
    public bool Invoke(ContactInfo contactInfo)
        => Filter?.Invoke(contactInfo.Chat) ?? true;

    public override string ToString()
        => $"{GetType()}({Id}, '{Title}')";

    public static ChatListFilter Parse(Symbol filterId)
        => AllKnown.FirstOrDefault(x => x.Id == filterId, None);

    // Equality

    public bool Equals(ChatListFilter? other)
        => !ReferenceEquals(null, other) && Id.Equals(other.Id);
    public override int GetHashCode()
        => Id.GetHashCode();
}

public static class ChatListSettingsFilterExt
{
    public static ChatListFilter GetFilter(this ChatListSettings settings)
        => ChatListFilter.Parse(settings.FilterId);
}
