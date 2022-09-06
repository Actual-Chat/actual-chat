using ActualChat.Users;
using ActualChat.Users.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RecentChats
{
    private IChats Chats { get; }
    private IRecentEntries RecentEntries { get; }
    private ChatUI ChatUI { get; }
    private Session Session { get; }
    private MomentClockSet Clocks { get; }
    private UICommander UICommander { get; }

    public RecentChats(
        IChats chats,
        IRecentEntries recentEntries,
        ChatUI chatUI,
        Session session,
        MomentClockSet clocks,
        UICommander uiCommander)
    {
        Chats = chats;
        RecentEntries = recentEntries;
        ChatUI = chatUI;
        Session = session;
        Clocks = clocks;
        UICommander = uiCommander;
    }

    [ComputeMethod]
    public virtual async Task<ImmutableArray<Chat>> List(CancellationToken cancellationToken = default)
    {
        var chats = await Chats.List(Session, cancellationToken).ConfigureAwait(false);
        chats = await RecentEntries.OrderByRecency(Session, chats, RecencyScope.ChatContact, cancellationToken).ConfigureAwait(false);
        var activeChat = await GetActiveChat(cancellationToken);
        if (activeChat != null && !chats.Contains(activeChat))
            chats = chats.Insert(0, activeChat);

        return chats;
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

    public async Task TryUpdatePeerChatRecency(Chat chat)
    {
        await Update(RecencyScope.ChatContact, chat.Id).ConfigureAwait(false);

        if (chat.ChatType == ChatType.Peer) {
            var contact = await Chats.GetPeerChatContact(Session, chat.Id, CancellationToken.None).Require().ConfigureAwait(false);
            await Update(RecencyScope.UserContact, contact.Id).ConfigureAwait(false);
        }

        Task Update(RecencyScope scope, string key) {
            var command = new IRecentEntries.UpdateCommand(Session, scope, key, Clocks.SystemClock.UtcNow);
            return UICommander.Run(command, CancellationToken.None);
        }
    }
}
