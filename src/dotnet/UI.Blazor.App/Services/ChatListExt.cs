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
        IReadOnlyDictionary<ChatId, Moment>? liftedAt = null)
    {
        // liftedAt holds the chats lifted above the rest, newest first: reacted chats in the
        // notifications panel, pinged ones on its Mentions tab. ByLastEventTime sorts on a version,
        // not a Moment, so the lifting time can't be folded into it.
        var preOrderedChats = preOrder switch {
            ChatListPreOrder.ChatList => PreOrderChatListFor(chats, order, liftedAt),
            ChatListPreOrder.None => chats.ToFakeOrderedEnumerable(),
            ChatListPreOrder.NotesFirst => chats
                .OrderByDescending(c => c.Chat.SystemTag == Constants.Chat.SystemTags.Notes),
            ChatListPreOrder.ReactionsFirst => LiftBy(chats, liftedAt),
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

    // Private methods

    private static IOrderedEnumerable<ChatInfo> PreOrderChatListFor(
        this IEnumerable<ChatInfo> chats,
        ChatListOrder order,
        IReadOnlyDictionary<ChatId, Moment>? liftedAt)
        => order switch {
            ChatListOrder.ByLastEventTime => PreOrderChats(chats, liftedAt),
            ChatListOrder.ByOwnUpdateTime => PreOrderChats(chats, liftedAt),
            ChatListOrder.ByUnreadCount => PreOrderChats(chats, liftedAt),
            ChatListOrder.ByAlphabet => chats.OrderByDescending(c => c.Contact.IsPinned),
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };

    private static IOrderedEnumerable<ChatInfo> PreOrderChats(
        IEnumerable<ChatInfo> chats,
        IReadOnlyDictionary<ChatId, Moment>? liftedAt)
        => LiftBy(chats, liftedAt)
            .ThenByDescending(c => c.Contact.IsPinned)
            .ThenByDescending(c => c.HasUnreadMentions);

    private static IOrderedEnumerable<ChatInfo> LiftBy(
        IEnumerable<ChatInfo> chats,
        IReadOnlyDictionary<ChatId, Moment>? liftedAt)
        => chats
            .OrderByDescending(c => GetLiftedAt(liftedAt, c.Id) is not null)
            .ThenByDescending(c => GetLiftedAt(liftedAt, c.Id) ?? Moment.EpochStart);

    private static Moment? GetLiftedAt(IReadOnlyDictionary<ChatId, Moment>? liftedAt, ChatId chatId)
        => liftedAt is not null && liftedAt.TryGetValue(chatId, out var moment) ? moment : null;
}
