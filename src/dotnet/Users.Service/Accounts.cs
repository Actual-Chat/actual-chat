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
    public virtual async Task Update(IAccounts.UpdateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, account, expectedVersion) = command;

        await this.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new IAccountsBackend.UpdateCommand(account, expectedVersion), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task InvalidateEverything(
        IAccounts.InvalidateEverythingCommand command,
        CancellationToken cancellationToken)
    {
        var (session, everywhere) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            // It should happen inside this block to make sure it runs on every node
            var agentInfo = Services.GetRequiredService<AgentInfo>();
            var operation = context.Operation();
            if (everywhere || operation.AgentId == agentInfo.Id)
                ComputedRegistry.Instance.InvalidateEverything();
            return;
        }

        var account = await GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);

        // We must call CreateCommandDbContext to make sure this operation is logged in the Users DB
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
    }
}
