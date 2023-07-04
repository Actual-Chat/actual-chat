namespace ActualChat.Chat.UI.Blazor.Services;

public sealed record ChatListFilter(
    Symbol Id,
    string Title,
    Func<ChatInfo, bool>? Filter = null
) {
    public static readonly ChatListFilter None = new("", "All", _ => true);
    public static readonly ChatListFilter Personal = new("@personal", "Personal", c => c.Chat.Kind == ChatKind.Peer);
    public static readonly ChatListFilter Groups = new("@groups", "Groups", c => c.Chat.Kind != ChatKind.Peer);
    public static readonly ApiArray<ChatListFilter> All = new(None, Personal, Groups);

    public override string ToString()
        => $"{GetType()}({Id}, '{Title}')";

    public static ChatListFilter Parse(Symbol filterId)
        => All.FirstOrDefault(x => x.Id == filterId, new ChatListFilter(filterId, filterId.Value));

    // Equality

    public bool Equals(ChatListFilter? other)
        => !ReferenceEquals(null, other) && Id.Equals(other.Id);
    public override int GetHashCode()
        => Id.GetHashCode();
}
