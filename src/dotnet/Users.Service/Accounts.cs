using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Notification;
using ActualChat.Users.Db;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class Accounts(IServiceProvider services) : DbServiceBase<UsersDbContext>(services), IAccounts
{
    private IAuth Auth { get; } = services.GetRequiredService<IAuth>();
    private IAccountsBackend Backend { get; } = services.GetRequiredService<IAccountsBackend>();

    // [ComputeMethod]
    public virtual async Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        UserId userId;
        if (user == null) {
            var sessionInfo = await Auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
            if (sessionInfo == null)
                throw StandardError.NotFound<Session>();

            userId = sessionInfo.GetGuestId();
            if (!userId.IsGuest)
                throw StandardError.Internal("GuestId is not set.");
        }
        else
            userId = new UserId(user.Id);

        var account = await Backend.Get(userId, cancellationToken).Require().ConfigureAwait(false);
        return account;
    }

    // [ComputeMethod]
    public virtual async Task<Account?> Get(Session session, UserId userId, CancellationToken cancellationToken)
    {
        var account = await Backend.Get(userId, cancellationToken).ConfigureAwait(false);
        return account.ToAccount();
    }

    // [ComputeMethod]
    public virtual async Task<AccountFull?> GetFull(Session session, UserId userId, CancellationToken cancellationToken)
    {
        var account = await Backend.Get(userId, cancellationToken).ConfigureAwait(false);
        await this.AssertCanRead(session, account, cancellationToken).ConfigureAwait(false);
        return account;
    }

    // [CommandHandler]
    public virtual async Task OnUpdate(Accounts_Update command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, account, expectedVersion) = command;

        await this.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new AccountsBackend_Update(account, expectedVersion), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDeleteOwn(Accounts_DeleteOwn command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var ownAccount = await GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        ownAccount.Require(AccountFull.MustBeActive);

        // sign out to prevent unexpected UI invalidations
        var signOutCommand = new Auth_SignOut(command.Session, null, false, false);
        await Commander.Call(signOutCommand, cancellationToken).ConfigureAwait(false);

        var deleteOwnChatsCommand = new ChatsBackend_RemoveOwnChats(ownAccount.Id);
        await Commander.Call(deleteOwnChatsCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteOwnMessagesCommand = new ChatsBackend_RemoveOwnEntries(ownAccount.Id);
        await Commander.Call(deleteOwnMessagesCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteNotificationsCommand = new NotificationsBackend_RemoveAccount(ownAccount.Id);
        await Commander.Call(deleteNotificationsCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteContactsCommand = new ContactsBackend_RemoveAccount(ownAccount.Id);
        await Commander.Call(deleteContactsCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteExternalContactsCommand = new ExternalContactsBackend_RemoveAccount(ownAccount.Id);
        await Commander.Call(deleteExternalContactsCommand, true, cancellationToken).ConfigureAwait(false);

        var deleteOwnAccountCommand = new AccountsBackend_Delete(ownAccount.Id);
        await Commander.Call(deleteOwnAccountCommand, false, cancellationToken).ConfigureAwait(false);
    }
}
