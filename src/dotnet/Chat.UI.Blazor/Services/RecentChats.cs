using ActualChat.Users;
using ActualChat.Users.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RecentChats
{
    private IChats Chats { get; }
    private UnreadMessagesFactory UnreadMessagesFactory { get; }
    private IRecentEntries RecentEntries { get; }
    private ChatUI ChatUI { get; }
    private Session Session { get; }

    public RecentChats(
        IChats chats,
        IRecentEntries recentEntries,
        ChatUI chatUI,
        Session session,
        UnreadMessagesFactory unreadMessagesFactory)
    {
        Chats = chats;
        RecentEntries = recentEntries;
        ChatUI = chatUI;
        Session = session;
        UnreadMessagesFactory = unreadMessagesFactory;
    }

    [ComputeMethod]
    public virtual async Task<ImmutableArray<Chat>> List(CancellationToken cancellationToken = default)
    {
        var chats = await Chats.List(Session, cancellationToken).ConfigureAwait(false);
        var result = new List<Chat>(chats.Length + 1);
        var activeChat = await GetActiveChat(cancellationToken);
        if (activeChat != null && !chats.Contains(activeChat))
            result.Insert(0, activeChat);

        var chatIdsWithMentions = chats.Zip(
                await chats.Select(HasMentions)
                    .Collect()
                    .ConfigureAwait(false))
            .Where(x => x.Second)
            .Select(x => x.First.Id)
            .ToHashSet();
        var chatsWithMentions = chats.Where(x => chatIdsWithMentions.Contains(x.Id)).ToList();
        var chatsWithoutMentions = chats.Where(x => !chatIdsWithMentions.Contains(x.Id)).ToList();
        result.AddRange(await OrderByRecency(chatsWithMentions).ConfigureAwait(false));
        result.AddRange(await OrderByRecency(chatsWithoutMentions).ConfigureAwait(false));

        return result.ToImmutableArray();

        Task<List<Chat>> OrderByRecency(List<Chat> items)
            => RecentEntries.OrderByRecency(Session,
                items,
                RecencyScope.ChatContact,
                chats.Length,
                cancellationToken);

        async Task<bool> HasMentions(Chat chat)
        {
            using var unreadMessages = UnreadMessagesFactory.Get(chat.Id);
            return await unreadMessages.HasMentions(cancellationToken).ConfigureAwait(false);
        }
    }

    [ComputeMethod]
    public virtual async Task<Chat?> GetActiveChat(CancellationToken cancellationToken)
    {
        var activeChatId = await ChatUI.ActiveChatId.Use(cancellationToken).ConfigureAwait(false);
        if (!ParsedChatId.TryParse(activeChatId, out _))
            activeChatId = Symbol.Empty;
        if (activeChatId.IsEmpty)
            return null;

        return await Chats.Get(Session, activeChatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Chat?> GetActiveChatOrDefault(CancellationToken cancellationToken)
    {
        var activeChat = await GetActiveChat(cancellationToken).ConfigureAwait(false);
        if (activeChat != null)
            return activeChat;

        var chats = await List(cancellationToken).ConfigureAwait(false);
        return chats.FirstOrDefault(x => x.Id == Constants.Chat.DefaultChatId) ?? chats.FirstOrDefault();
    }
}
