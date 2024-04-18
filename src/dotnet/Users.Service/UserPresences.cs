namespace ActualChat.Users;

public class UserPresences(IServiceProvider services) : IUserPresences
{
    private static readonly TimeSpan SessionUpdatePeriod = TimeSpan.FromHours(1);

    private IUserPresencesBackend Backend { get; } = services.GetRequiredService<IUserPresencesBackend>();
    private IAuth Auth { get; } = services.GetRequiredService<IAuth>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private ICommander Commander { get; } = services.Commander();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private Moment SystemNow => Clocks.SystemClock.Now;

    // [ComputeMethod]
    public virtual async Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
        => await Backend.Get(userId, cancellationToken).ConfigureAwait(false);

    // [ComputeMethod]
    public virtual async Task<Moment?> GetLastCheckIn(UserId userId, CancellationToken cancellationToken)
        => await Backend.GetLastCheckIn(userId, cancellationToken).ConfigureAwait(false);

    // [CommandHandler]
    public virtual async Task OnCheckIn(UserPresences_CheckIn command, CancellationToken cancellationToken)
    {
        var (session, isActive) = command;

        var sessionInfo = await Auth.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo != null && SystemNow - sessionInfo.LastSeenAt > SessionUpdatePeriod) {
            var setupSessionCommand = new AuthBackend_SetupSession(session);
            await Commander.Call(setupSessionCommand, true, cancellationToken).ConfigureAwait(false);
        }

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return;

        var backendCommand = new UserPresencesBackend_CheckIn(account.Id, SystemNow, isActive);
        _ = Commander.Run(backendCommand, true, CancellationToken.None);
    }
}
