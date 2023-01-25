namespace ActualChat.Chat.UI.Blazor.Services;

public sealed record ChatListFilter(
    Symbol Id,
    string Title,
    bool IsSystem = false
) {
    public static readonly ChatListFilter None = new("", "All", true);
    public static readonly ChatListFilter Personal = new("@personal", "Personal", true);
    public static readonly ImmutableArray<ChatListFilter> All = ImmutableArray.Create(None, Personal);

    public static ChatListFilter Parse(Symbol filterId)
        => All.FirstOrDefault(x => x.Id == filterId, new ChatListFilter(filterId, filterId.Value));
}
