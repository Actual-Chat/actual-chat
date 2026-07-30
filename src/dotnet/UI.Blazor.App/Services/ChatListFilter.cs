namespace ActualChat.UI.Blazor.App.Services;

public sealed record ChatListFilter(
    Symbol Id,
    string Title,
    Func<Chat.Chat, bool>? Filter = null
) {
    public static readonly ChatListFilter None = new("", "All", _ => true);
    public static readonly ChatListFilter People = new("@people", "People", c => c.Kind == ChatKind.Peer);
    public static readonly ChatListFilter Groups = new("@groups", "Groups", c => c.Kind != ChatKind.Peer);
    public static readonly ChatListFilter[] All = [None, Groups, People];

    public bool Invoke(ChatInfo chatInfo)
        => Filter?.Invoke(chatInfo.Chat) ?? true;
    public bool Invoke(ContactInfo contactInfo)
        => Filter?.Invoke(contactInfo.Chat) ?? true;

    public override string ToString()
        => $"{GetType()}({Id}, '{Title}')";

    public static ChatListFilter Parse(Symbol filterId)
        => All.FirstOrDefault(x => x.Id == filterId, None);

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
