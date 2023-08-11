namespace ActualChat.Users;

public class UserPresences : IUserPresences
{
    private IUserPresencesBackend Backend { get; }
    private IAuth Auth { get; }
    private IAccounts Accounts { get; }
    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public UserPresences(IServiceProvider services)
    {
        Backend = services.GetRequiredService<IUserPresencesBackend>();
        Auth = services.GetRequiredService<IAuth>();
        Accounts = services.GetRequiredService<IAccounts>();
        Commander = services.Commander();
        Clocks = services.Clocks();
    }

    // [ComputeMethod]
    public virtual async Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
        => await Backend.Get(userId, cancellationToken).ConfigureAwait(false);

    // [CommandHandler]
    public virtual async Task OnCheckIn(UserPresences_CheckIn command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (session, isActive) = command;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return;

        var backendCommand = new UserPresencesBackend_CheckIn(account.Id, Now, isActive);
        _ = Commander.Run(backendCommand, true, CancellationToken.None);

        // we should await this call as it uses DBContext and we can face concurrent issues with single connection
        await Auth.UpdatePresence(session, cancellationToken).ConfigureAwait(false);
    }
}
