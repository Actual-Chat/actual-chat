namespace ActualChat.Users;

public class UserPresences : IUserPresences
{
    private IUserPresencesBackend Backend { get; }
    private IAccounts Accounts { get; }
    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public UserPresences(IServiceProvider services)
    {
        Backend = services.GetRequiredService<IUserPresencesBackend>();
        Accounts = services.GetRequiredService<IAccounts>();
        Commander = services.Commander();
        Clocks = services.Clocks();
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

        var backendCommand = new IUserPresencesBackend.CheckInCommand(account.Id, Now);
        await Commander.Call(backendCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
