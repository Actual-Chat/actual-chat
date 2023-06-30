using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class Accounts : DbServiceBase<UsersDbContext>, IAccounts
{
    private IAuth Auth { get; }
    private IAccountsBackend Backend { get; }

    public Accounts(IServiceProvider services) : base(services)
    {
        Auth = services.GetRequiredService<IAuth>();
        Backend = services.GetRequiredService<IAccountsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<AccountFull> GetOwn(Session session, CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        UserId userId;
        if (user == null) {
            var sessionInfo = await Auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
            if (sessionInfo == null)
                throw StandardError.WrongSession("Session is not found.");

            userId = sessionInfo.GetGuestId();
            if (!userId.IsGuest)
                throw StandardError.WrongSession("GuestId is not set.");
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
}
