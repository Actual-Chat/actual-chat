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
        => chats.WithSearchMatchRank(searchPhrase, c => c.Chat.Title);

    public static IEnumerable<(ChatInfo ChatInfo, double Rank)> FilterBySearchMatchRank(
        this IEnumerable<(ChatInfo ChatInfo, double Rank)> rankedChats,
        ChatId selectedChatId = default)
        => selectedChatId.IsNone
            ? rankedChats.Where(x => x.Rank > 0)
            : rankedChats.Where(x => x.ChatInfo.Id == selectedChatId || x.Rank > 0);

    public static IEnumerable<ChatInfo> OrderBy(
        this IEnumerable<ChatInfo> chats,
        ChatListOrder order,
        ChatListPreOrder preOrder)
    {
        var preOrderedChats = preOrder switch {
            ChatListPreOrder.ChatList => PreOrderChatListFor(chats, order),
            ChatListPreOrder.None => chats.ToFakeOrderedEnumerable(),
            ChatListPreOrder.NotesFirst => chats.OrderByDescending(c => c.Chat.SystemTag == Constants.Chat.SystemTags.Notes),
            _ => throw new ArgumentOutOfRangeException(nameof(preOrder)),
        };
        return order switch {
            ChatListOrder.ByLastEventTime => preOrderedChats
                .ThenByDescending(c => c.News.LastTextEntry?.Version ?? c.Contact.Version),
            ChatListOrder.ByOwnUpdateTime => preOrderedChats
                .ThenByDescending(c => c.Contact.Version),
            ChatListOrder.ByUnreadCount => preOrderedChats
                .ThenByDescending(c => c.UnreadCount.Value)
                .ThenByDescending(c => c.News.LastTextEntry?.Version ?? c.Contact.Version),
            ChatListOrder.ByAlphabet => preOrderedChats
                .OrderByDescending(c => c.Contact.IsPinned)
                .ThenBy(c => c.Chat.Title, StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
    }

    private static IOrderedEnumerable<ChatInfo> PreOrderChatListFor(
        this IEnumerable<ChatInfo> chats,
        ChatListOrder order)
        => order switch {
            ChatListOrder.ByLastEventTime => PreOrderChats(chats),
            ChatListOrder.ByOwnUpdateTime => PreOrderChats(chats),
            ChatListOrder.ByUnreadCount => PreOrderChats(chats),
            ChatListOrder.ByAlphabet => chats.OrderByDescending(c => c.Contact.IsPinned),
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };

    private static IOrderedEnumerable<ChatInfo> PreOrderChats(
        IEnumerable<ChatInfo> chats)
        => chats
            .OrderByDescending(c => c.Contact.IsPinned)
            .ThenByDescending(c => c.HasUnreadMentions);
}
