using ActualChat.Users.Db;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class Accounts : DbServiceBase<UsersDbContext>, IAccounts
{
    private readonly IAuth _auth;
    private readonly IAccountsBackend _backend;
    private readonly ICommander _commander;

    public Accounts(IServiceProvider services) : base(services)
    {
        _auth = services.GetRequiredService<IAuth>();
        _backend = services.GetRequiredService<IAccountsBackend>();
        _commander = services.GetRequiredService<ICommander>();
    }

    // [ComputeMethod]
    public virtual async Task<Account?> Get(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return null;

        return await _backend.Get(user.Id, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Account?> GetByUserId(Session session, string userId, CancellationToken cancellationToken)
    {
        var account = await _backend.Get(userId, cancellationToken).ConfigureAwait(false);
        await this.AssertCanRead(session, account, cancellationToken).ConfigureAwait(false);
        return account;
    }

    public virtual Task<UserAuthor?> GetUserAuthor(string userId, CancellationToken cancellationToken)
        => _backend.GetUserAuthor(userId, cancellationToken);

    // [CommandHandler]
    public virtual async Task Update(IAccounts.UpdateCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, account) = command;

        await this.AssertCanUpdate(session, account, cancellationToken).ConfigureAwait(false);
        await _commander.Call(new IAccountsBackend.UpdateCommand(command.Account), cancellationToken)
            .ConfigureAwait(false);
    }
}
