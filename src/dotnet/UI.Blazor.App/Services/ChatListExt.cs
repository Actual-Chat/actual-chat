using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public static class ChatListExt
{
    public static IEnumerable<ChatInfo> FilterAndOrderBySearchQuery(
        this IEnumerable<ChatInfo> chats,
        SearchQuery query,
        ChatId? selectedChatId = null)
    {
        if (!query.IsEmpty)
            chats = chats
                .WithSearchMatchRank(query, c => c.SearchDocument)
                .FilterBySearchMatchRank(selectedChatId)
                .OrderBySearchMatchRank()
                .WithoutSearchMatchRank();
        return chats;
    }

    public static IEnumerable<ContactInfo> FilterAndOrderBySearchQuery(
        this IEnumerable<ContactInfo> contacts,
        SearchQuery query)
    {
        if (!query.IsEmpty)
            contacts = contacts
                .WithSearchMatchRank(query, c => c.SearchDocument)
                .Where(x => x.Rank > 0)
                .OrderBySearchMatchRank()
                .WithoutSearchMatchRank();
        return contacts;
    }

    public static IEnumerable<(ChatInfo ChatInfo, double Rank)> FilterBySearchMatchRank(
        this IEnumerable<(ChatInfo ChatInfo, double Rank)> rankedChats,
        ChatId? selectedChatId = null)
        => selectedChatId is null
            ? rankedChats.Where(x => x.Rank > 0)
            : rankedChats.Where(x => x.ChatInfo.Id == selectedChatId || x.Rank > 0);

    public static IEnumerable<ChatInfo> OrderBy(
        this IEnumerable<ChatInfo> chats,
        ChatListOrder order,
        ChatListPreOrder preOrder,
        IReadOnlyDictionary<ChatId, Moment>? reactedAt = null)
    {
        var preOrderedChats = preOrder switch {
            ChatListPreOrder.ChatList => PreOrderChatListFor(chats, order),
            ChatListPreOrder.None => chats.ToFakeOrderedEnumerable(),
            ChatListPreOrder.NotesFirst => chats
                .OrderByDescending(c => c.Chat.SystemTag == Constants.Chat.SystemTags.Notes),
            // ByLastEventTime sorts on a version, not a Moment, so a reaction time can't be folded
            // into it - a reacted chat is lifted above the rest instead.
            ChatListPreOrder.ReactionsFirst => chats
                .OrderByDescending(c => GetReactedAt(reactedAt, c.Id) is not null)
                .ThenByDescending(c => GetReactedAt(reactedAt, c.Id) ?? Moment.EpochStart),
            _ => throw new ArgumentOutOfRangeException(nameof(preOrder)),
        };
        return order switch {
            ChatListOrder.ByLastEventTime => preOrderedChats
                .ThenByDescending(c => c.LastTextEntry?.Version ?? c.Contact.Version),
            ChatListOrder.ByOwnUpdateTime => preOrderedChats
                .ThenByDescending(c => c.Contact.Version),
            ChatListOrder.ByUnreadCount => preOrderedChats
                .ThenByDescending(c => c.UnreadCount.Value)
                .ThenByDescending(c => c.LastTextEntry?.Version ?? c.Contact.Version),
            ChatListOrder.ByAlphabet => preOrderedChats
                .OrderByDescending(c => c.Contact.IsPinned)
                .ThenBy(c => c.Chat.Title),
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
    }

    public static IEnumerable<ContactInfo> OrderBy(
        this IEnumerable<ContactInfo> contacts,
        ChatListOrder order,
        ChatListPreOrder preOrder)
    {
        // Orders needing news/unread state degrade to ByOwnUpdateTime: pickers don't load it.
        var preOrderedContacts = preOrder switch {
            ChatListPreOrder.ChatList => contacts.OrderByDescending(c => c.Contact.IsPinned),
            ChatListPreOrder.None => contacts.ToFakeOrderedEnumerable(),
            ChatListPreOrder.NotesFirst => contacts.OrderByDescending(
                c => c.Chat.SystemTag == Constants.Chat.SystemTags.Notes),
            _ => throw new ArgumentOutOfRangeException(nameof(preOrder)),
        };
        return order switch {
            ChatListOrder.ByAlphabet => preOrderedContacts
                .OrderByDescending(c => c.Contact.IsPinned)
                .ThenBy(c => c.Chat.Title),
            _ => preOrderedContacts
                .ThenByDescending(c => c.Contact.Version),
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

    private static Moment? GetReactedAt(IReadOnlyDictionary<ChatId, Moment>? reactedAt, ChatId chatId)
        => reactedAt is not null && reactedAt.TryGetValue(chatId, out var moment) ? moment : null;
}
