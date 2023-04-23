using ActualChat.Search;

namespace ActualChat.Chat.UI.Blazor.Services;

public static class ChatListExt
{
    public static IEnumerable<ChatInfo> FilterBySearchPhrase(
        this IEnumerable<ChatInfo> chats,
        SearchPhrase searchPhrase,
        ChatId selectedChatId = default)
    {
        if (!searchPhrase.IsEmpty)
            chats =  chats
                .WithSearchMatchRank(searchPhrase)
                .FilterBySearchMatchRank(selectedChatId)
                .WithoutSearchMatchRank();
        return chats;
    }

    public static IEnumerable<ChatInfo> FilterAndOrderBySearchPhrase(
        this IEnumerable<ChatInfo> chats,
        SearchPhrase searchPhrase,
        ChatId selectedChatId = default)
    {
        if (!searchPhrase.IsEmpty)
            chats =  chats
                .WithSearchMatchRank(searchPhrase)
                .FilterBySearchMatchRank(selectedChatId)
                .OrderBySearchMatchRank()
                .WithoutSearchMatchRank();
        return chats;
    }

    public static IEnumerable<(ChatInfo ChatInfo, double Rank)> WithSearchMatchRank(
        this IEnumerable<ChatInfo> chats,
        SearchPhrase searchPhrase)
        => chats.Select(c => (ChatInfo: c, Rank: searchPhrase.GetMatchRank(c.Chat.Title)));

    public static IEnumerable<ChatInfo> WithoutSearchMatchRank(
        this IEnumerable<(ChatInfo ChatInfo, double Rank)> rankedChats)
        => rankedChats.Select(c => c.ChatInfo);

    public static IEnumerable<(ChatInfo ChatInfo, double Rank)> FilterBySearchMatchRank(
        this IEnumerable<(ChatInfo ChatInfo, double Rank)> rankedChats,
        ChatId selectedChatId = default)
        => selectedChatId.IsNone
            ? rankedChats.Where(x => x.Rank > 0)
            : rankedChats.Where(x => x.ChatInfo.Id == selectedChatId || x.Rank > 0);

    public static IEnumerable<(ChatInfo ChatInfo, double Rank)> OrderBySearchMatchRank(
        this IEnumerable<(ChatInfo ChatInfo, double Rank)> rankedChats)
        => rankedChats.OrderByDescending(x => x.Rank);

    public static IEnumerable<ChatInfo> OrderBy(
        this IEnumerable<ChatInfo> chats,
        ChatListOrder order)
        => order switch {
            ChatListOrder.ByLastEventTime => PreOrderChats(chats)
                .ThenByDescending(c => c.News.LastTextEntry?.Version ?? 0),
            ChatListOrder.ByOwnUpdateTime => PreOrderChats(chats)
                .ThenByDescending(c => c.Contact.TouchedAt),
            ChatListOrder.ByUnreadCount => PreOrderChats(chats)
                .ThenByDescending(c => c.UnreadCount.Value)
                .ThenByDescending(c => c.News.LastTextEntry?.Version),
            ChatListOrder.ByAlphabet => chats
                .OrderByDescending(c => c.Contact.IsPinned)
                .ThenBy(c => c.Chat.Title, StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };

    private static IOrderedEnumerable<ChatInfo> PreOrderChats(
        IEnumerable<ChatInfo> chats)
        => chats
            .OrderByDescending(c => c.Contact.IsPinned)
            .ThenByDescending(c => c.HasUnreadMentions);
}
