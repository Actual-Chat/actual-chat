using ActualChat.Users;
using ActualChat.Users.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RecentChatsUI : WorkerBase
{
    private volatile ImmutableList<Chat> _listIncludingSelectedCached = ImmutableList<Chat>.Empty;

    private IChats Chats { get; }
    private UnreadMessages UnreadMessages { get; }
    private IRecentEntries RecentEntries { get; }
    private ChatUI ChatUI { get; }
    private Session Session { get; }

    public RecentChatsUI(
        IChats chats,
        UnreadMessages unreadMessages,
        IRecentEntries recentEntries,
        ChatUI chatUI,
        Session session)
    {
        Chats = chats;
        UnreadMessages = unreadMessages;
        RecentEntries = recentEntries;
        ChatUI = chatUI;
        Session = session;
        Start();
    }

    [ComputeMethod]
    public virtual Task<ImmutableList<Chat>> ListIncludingSelected()
        => Task.FromResult(_listIncludingSelectedCached);

    [ComputeMethod]
    public virtual async Task<Chat?> GetSelectedChat(CancellationToken cancellationToken)
    {
        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (!ParsedChatId.TryParse(selectedChatId, out _))
            selectedChatId = Symbol.Empty;
        if (selectedChatId.IsEmpty)
            return null;

        return await Chats.Get(Session, selectedChatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Chat?> GetSelectedOrDefaultChat(CancellationToken cancellationToken)
    {
        var selectedChat = await GetSelectedChat(cancellationToken).ConfigureAwait(false);
        if (selectedChat != null)
            return selectedChat;

        var chats = await ListIncludingSelected().ConfigureAwait(false);
        return chats.FirstOrDefault(x => x.Id == Constants.Chat.DefaultChatId) ?? chats.FirstOrDefault();
    }

    [ComputeMethod]
    protected virtual async Task<ImmutableList<Chat>> List(CancellationToken cancellationToken)
    {
        var chats = await Chats.List(Session, cancellationToken).ConfigureAwait(false);
        var chatIdsWithMentions = (await chats
            .Select(async chat => {
                var hasMentions = await UnreadMessages.HasMentions(chat.Id, cancellationToken).ConfigureAwait(false);
                return (Chat: chat, HasMentions: hasMentions);
            })
            .Collect()
            ).Where(x => x.HasMentions)
            .Select(x => x.Chat.Id)
            .ToHashSet();
        var chatsWithMentions = chats.Where(x => chatIdsWithMentions.Contains(x.Id)).ToList();
        var chatsWithoutMentions = chats.Where(x => !chatIdsWithMentions.Contains(x.Id)).ToList();
        chatsWithMentions = await OrderByRecency(chatsWithMentions).ConfigureAwait(false);
        chatsWithoutMentions = await OrderByRecency(chatsWithoutMentions).ConfigureAwait(false);

        return chatsWithMentions.Concat(chatsWithoutMentions).ToImmutableList();

        Task<List<Chat>> OrderByRecency(List<Chat> items)
            => RecentEntries.OrderByRecency(Session,
                items,
                RecencyScope.Chat,
                chats.Length,
                cancellationToken);
    }

    protected override Task RunInternal(CancellationToken cancellationToken)
        => InvalidateListIncludingSelected(cancellationToken);

    [ComputeMethod]
    protected virtual async Task<ImmutableList<Chat>> ListIncludingSelectedInternal(CancellationToken cancellationToken)
    {
        var selectedChat = await GetSelectedChat(cancellationToken).ConfigureAwait(false);
        var chats = await List(cancellationToken).ConfigureAwait(false);
        if (selectedChat != null && !chats.Contains(selectedChat))
            chats = chats.Insert(0, selectedChat);

        return chats;
    }

    private async Task InvalidateListIncludingSelected(CancellationToken cancellationToken)
    {
        var cListIncludingSelected = await Computed
            .Capture(() => ListIncludingSelectedInternal(cancellationToken))
            .ConfigureAwait(false);
        var changes = cListIncludingSelected.Changes(cancellationToken);
        await foreach (var c in changes.ConfigureAwait(false)) {
            var listIncludingSelected = c.Value;

            if (!listIncludingSelected.SequenceEqual(_listIncludingSelectedCached)) {
                Interlocked.Exchange(ref _listIncludingSelectedCached, listIncludingSelected);
                using (Computed.Invalidate())
                    _ = ListIncludingSelected();
            }
        }
    }
}
