using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Services;

public class AuthorUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private IAccounts Accounts => Hub.Accounts;
    private IAuthors Authors => Hub.Authors;
    private IContacts Contacts => Hub.Contacts;

    [ComputeMethod]
    public virtual Task<AuthorFull> GetOwn(ChatId chatId, CancellationToken cancellationToken)
        => Authors.GetOwn(Session, chatId, cancellationToken).Require();

    [ComputeMethod]
    public virtual async Task<string> GetUserName(ChatId chatId, UserId userId, CancellationToken cancellationToken)
    {
        // The name you see for this user: their avatar name in this chat if they're a member of it,
        // otherwise your rename of them. Both already account for a rename; "" means neither applies.
        if (userId.IsGuestOrNull())
            return "";

        var author = await Authors.GetByUserId(Session, chatId, userId, cancellationToken).ConfigureAwait(false);
        if (author is not null)
            return author.Avatar.Name;

        var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        if (ownAccount.IsGuestOrNull())
            return "";

        var contactId = ContactId.NewUser(ownAccount.Id, userId);
        var contact = await Contacts.Get(Session, contactId, cancellationToken).ConfigureAwait(false);
        return contact?.PreferredPeerName ?? "";
    }

    public Task<ModalRef> Show(AuthorId authorId)
        => ModalUI.Show(new AuthorModal.Model(authorId), CancellationToken.None);

    public async Task StartPeerChat(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        var peerChatId = await GetPeerChatId(authorId, cancellationToken).ConfigureAwait(true);
        if (peerChatId is null)
            return;

        var localUrl = Links.Chat(peerChatId);
        _ = History.NavigateTo(localUrl);
    }

    public async Task<PeerChatId?> GetPeerChatId(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        var ownAccountTask = Accounts.GetOwn(Session, cancellationToken);
        var accountTask = Authors.GetAccount(Session, authorId.ChatId, authorId, cancellationToken);
        var ownAccount = await ownAccountTask.ConfigureAwait(false);
        var account = await accountTask.ConfigureAwait(false);
        var canStartPeerChat = account != null
            && !account.IsGuestOrNull()
            && !ownAccount.IsGuestOrNull()
            && account.Id != ownAccount.Id;
        if (!canStartPeerChat)
            return null;

        return PeerChatId.New(ownAccount.Id, account!.Id);
    }

    public async Task StartAnonymousPeerChat(AuthorId authorId, CancellationToken cancellationToken = default)
    {
        var account = await Authors
            .GetAccount(Session, authorId.ChatId, authorId, cancellationToken)
            .ConfigureAwait(true);
        if (account == null)
            return;

        await StartAnonymousPeerChat(account.Id, cancellationToken).ConfigureAwait(true);
    }

    public async Task StartAnonymousPeerChat(UserId userId, CancellationToken cancellationToken = default)
    {
        var now = Clocks.SystemClock.Now;
        var sDate = now.ToDateTime().ToString("MM/dd/yyyy");
        var createCommand = new Chats_Change {
            Session = Session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = $"Anonymous chat ({sDate})",
                    Kind = ChatKind.Group,
                    IsPublic = false,
                    AllowAnonymousAuthors = true,
                },
            },
        };
        var chatResult = await UICommander.Run(createCommand, cancellationToken).ConfigureAwait(true);
        if (chatResult.HasError)
            return;

        var chatId = chatResult.Value.Id;
        var addOtherUserCommand = new Authors_Invite {
            Session = Session,
            ChatId = chatId,
            UserIds = new[] { userId },
            JoinAnonymously = true,
        };
        var authorResult = await UICommander.Run(addOtherUserCommand, cancellationToken).ConfigureAwait(true);
        if (authorResult.HasError)
            return;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(true);
        var authorIds = await Authors.ListAuthorIds(Session, chatId, cancellationToken).ConfigureAwait(true);
        var otherAuthorId = authorIds.First(id => id != ownAuthor?.Id);
        var promoteCommand = new Authors_PromoteToOwner { Session = Session, AuthorId = otherAuthorId };
        var promoteResult = await UICommander.Run(promoteCommand, cancellationToken).ConfigureAwait(true);
        if (promoteResult.HasError)
            return;

        _ = History.NavigateTo(Links.Chat(chatId));
    }
}
