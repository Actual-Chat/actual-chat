using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class AuthorUI(ChatHub chatHub)
{
    private ILogger? _log;

    private ChatHub ChatHub { get; } = chatHub;
    private Session Session => ChatHub.Session;
    private IAccounts Accounts => ChatHub.Accounts;
    private IAuthors Authors => ChatHub.Authors;
    private ModalUI ModalUI => ChatHub.ModalUI;
    private History History => ChatHub.History;
    private UICommander UICommander => ChatHub.UICommander();
    private MomentClockSet Clocks => ChatHub.Clocks();
    private ILogger Log => _log ??= ChatHub.LogFor(GetType());
    private ILogger? DebugLog => Constants.DebugMode.ChatUI ? Log : null;

    public async Task Show(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        if (authorId.IsNone)
            return; // Likely the caller haven't read authorId yet, so we can't do much here

        await ModalUI.Show(new AuthorModal.Model(authorId), CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<bool> CanStartPeerChat(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        if (authorId.IsNone)
            return false;

        var ownAccountTask = Accounts.GetOwn(Session, cancellationToken);
        var accountTask = Authors.GetAccount(Session, authorId.ChatId, authorId, cancellationToken);
        var ownAccount = await ownAccountTask.ConfigureAwait(false);
        var account = await accountTask.ConfigureAwait(false);
        var canStartPeerChat = account != null
            && !account.IsGuestOrNone
            && !ownAccount.IsGuestOrNone
            && account.Id != ownAccount.Id;
        return canStartPeerChat;
    }

    public async Task StartPeerChat(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        if (authorId.IsNone)
            return;

        var ownAccountTask = Accounts.GetOwn(Session, cancellationToken);
        var accountTask = Authors.GetAccount(Session, authorId.ChatId, authorId, cancellationToken);
        var ownAccount = await ownAccountTask.ConfigureAwait(false);
        var account = await accountTask.ConfigureAwait(false);
        var peerChatId = new PeerChatId(ownAccount.Id, account!.Id);
        var localUrl = Links.Chat(peerChatId);
        _ = History.NavigateTo(localUrl);
    }

    public async Task StartAnonymousPeerChat(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        if (authorId.IsNone)
            return;

        var account = await Authors.GetAccount(Session, authorId.ChatId, authorId, cancellationToken).ConfigureAwait(true);
        if (account == null)
            return;

        await StartAnonymousPeerChat(account.Id, cancellationToken).ConfigureAwait(true);
    }

    public async Task StartAnonymousPeerChat(UserId userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsNone)
            return;

        var now = Clocks.SystemClock.Now;
        var sDate = now.ToDateTime().ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        var createCommand = new Chats_Change(Session, default, null, new() {
            Create = new ChatDiff {
                Title = $"Anonymous chat ({sDate})",
                Kind = ChatKind.Group,
                IsPublic = false,
                AllowAnonymousAuthors = true,
            },
        });
        var chatResult = await UICommander.Run(createCommand, cancellationToken).ConfigureAwait(true);
        if (chatResult.HasError)
            return;

        var chatId = chatResult.Value.Id;
        var addOtherUserCommand = new Authors_Invite(Session, chatId, new[] { userId }, JoinAnonymously: true);
        var authorResult = await UICommander.Run(addOtherUserCommand, cancellationToken).ConfigureAwait(true);
        if (authorResult.HasError)
            return;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(true);
        var authorIds = await Authors.ListAuthorIds(Session, chatId, cancellationToken).ConfigureAwait(true);
        var otherAuthorId = authorIds.Items.First(c => c.Id != ownAuthor!.Id);
        var promoteCommand = new Authors_PromoteToOwner(Session, otherAuthorId);
        var promoteResult = await UICommander.Run(promoteCommand, cancellationToken).ConfigureAwait(true);
        if (promoteResult.HasError)
            return;

        _ = History.NavigateTo(Links.Chat(chatId));
    }
}
