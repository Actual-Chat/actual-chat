namespace ActualChat.Users;

public class UserPresences : IUserPresences
{
    private IUserPresencesBackend Backend { get; }
    private IAccounts Accounts { get; }
    private ICommander Commander { get; }

    public UserPresences(IUserPresencesBackend backend, IAccounts accounts, ICommander commander, ILogger<UserPresences> log)
    {
        Backend = backend;
        Accounts = accounts;
        Commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
        => await Backend.Get(userId, cancellationToken).ConfigureAwait(false);

    // [CommandHandler]
    public virtual async Task CheckIn(IUserPresences.CheckInCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return;

        await Commander.Call(new IUserPresencesBackend.CheckInCommand(account.Id), cancellationToken).ConfigureAwait(false);
    }
}
