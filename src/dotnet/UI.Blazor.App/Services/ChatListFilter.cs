namespace ActualChat.UI.Blazor.App.Services;

public sealed record ChatListFilter(
    Symbol Id,
    string Title,
    Func<ChatInfo, bool>? Filter = null
) {
    public static readonly ChatListFilter None = new("", "All", _ => true);
    public static readonly ChatListFilter People = new("@people", "People", c => c.Chat.Kind == ChatKind.Peer);
    public static readonly ChatListFilter Groups = new("@groups", "Groups", c => c.Chat.Kind != ChatKind.Peer);
    public static readonly ApiArray<ChatListFilter> All = ApiArray.New(None, Groups, People);

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
