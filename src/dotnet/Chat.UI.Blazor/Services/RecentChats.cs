using ActualChat.Users;
using ActualChat.Users.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RecentChats : WorkerBase
{
    private ImmutableArray<Chat> _chatsIncludingActive = ImmutableArray<Chat>.Empty;
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
        Start();
    }

    [ComputeMethod]
    // optimized
    public virtual Task<ImmutableArray<Chat>> ListIncludingActive()
        => Task.FromResult(_chatsIncludingActive);

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

        var chats = await ListIncludingActive().ConfigureAwait(false);
        return chats.FirstOrDefault(x => x.Id == Constants.Chat.DefaultChatId) ?? chats.FirstOrDefault();
    }

    [ComputeMethod]
    protected virtual async Task<ImmutableArray<Chat>> List(CancellationToken cancellationToken)
    {
        var chats = await Chats.List(Session, cancellationToken).ConfigureAwait(false);
        var chatIdsWithMentions = chats.Zip(
                await chats.Select(HasMentions)
                    .Collect()
                    .ConfigureAwait(false))
            .Where(x => x.Second)
            .Select(x => x.First.Id)
            .ToHashSet();
        var chatsWithMentions = chats.Where(x => chatIdsWithMentions.Contains(x.Id)).ToList();
        var chatsWithoutMentions = chats.Where(x => !chatIdsWithMentions.Contains(x.Id)).ToList();
        chatsWithMentions = await OrderByRecency(chatsWithMentions).ConfigureAwait(false);
        chatsWithoutMentions = await OrderByRecency(chatsWithoutMentions).ConfigureAwait(false);

        return chatsWithMentions.Concat(chatsWithoutMentions).ToImmutableArray();

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

    protected override Task RunInternal(CancellationToken cancellationToken)
        => SyncListWithActiveChat(cancellationToken);

    [ComputeMethod]
    protected virtual async Task<ImmutableArray<Chat>> ListChatsIncludingActiveInternal(CancellationToken cancellationToken)
    {
        var activeChat = await GetActiveChat(cancellationToken).ConfigureAwait(false);
        var chats = await List(cancellationToken).ConfigureAwait(false);
        if (activeChat != null && !chats.Contains(activeChat))
            chats = chats.Insert(0, activeChat);

        return chats;
    }

    private async Task SyncListWithActiveChat(CancellationToken cancellationToken)
    {
        SetChats(await ListChatsIncludingActiveInternal(cancellationToken).ConfigureAwait(false));

        var changes = (await Computed.Capture(() => ListChatsIncludingActiveInternal(cancellationToken)).ConfigureAwait(false)).Changes(cancellationToken);
        await foreach (var cChats in changes.ConfigureAwait(false)) {
            var newChats = cChats.Value;

            if (!newChats.SequenceEqual(_chatsIncludingActive))
                SetChats(newChats);
        }

        void SetChats(ImmutableArray<Chat> newChats)
        {
            _chatsIncludingActive = newChats;
            using (Computed.Invalidate())
                _ = ListIncludingActive();
        }
    }
}
