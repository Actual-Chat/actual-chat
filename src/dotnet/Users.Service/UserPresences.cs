namespace ActualChat.Users;

/// <summary>
/// Frontend service for tracking user online/offline presence status.
/// </summary>
public class UserPresences(IServiceProvider services) : IUserPresences
{
    private static readonly TimeSpan SessionUpdatePeriod = TimeSpan.FromHours(1);
    private static readonly TimeSpan PresenceChangeDelay = TimeSpan.FromSeconds(0.25);

    private IUserPresencesBackend Backend { get; } = services.GetRequiredService<IUserPresencesBackend>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private ICommander Commander { get; } = services.Commander();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private Moment SystemNow => Clocks.SystemClock.Now;

    // [ComputeMethod]
    public virtual async Task<Presence> Get(UserId userId, CancellationToken cancellationToken)
    {
        if (Constants.User.Sherlock.UserId.Equals(userId))
            return Presence.Online;

        var now = SystemNow;
        var lastCheckIn = await GetLastCheckIn(userId, cancellationToken).ConfigureAwait(false);
        if (!lastCheckIn.IsValue(out var lastCheckInValue))
            return Presence.Unknown;

        var checkInRecency = now - lastCheckInValue;
        if (checkInRecency > Constants.Presence.OfflineTimeout)
            return Presence.Offline;

        var computed = Computed.GetCurrent();
        if (checkInRecency > Constants.Presence.AwayTimeout) {
            computed.Invalidate(Constants.Presence.OfflineTimeout - checkInRecency + PresenceChangeDelay);
            return Presence.Away;
        }

        computed.Invalidate(Constants.Presence.AwayTimeout - checkInRecency + PresenceChangeDelay);
        return Presence.Online;
    }

    // [ComputeMethod]
    public virtual async Task<ApiNullable8<Moment>> GetLastCheckIn(UserId userId, CancellationToken cancellationToken)
    {
        try {
            return await Backend
                .GetLastCheckIn(userId, cancellationToken)
                .WaitAsync(ServerConstants.Backend.Timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Computed.GetCurrent().Invalidate(ServerConstants.Backend.RetryDelay);
            return ApiNullable8<Moment>.Null;
        }
    }

    // [CommandHandler]
    public virtual async Task OnCheckIn(UserPresences_CheckIn command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var isActive = command.IsActive;
        if (!session.IsValid())
            return; // The client has no session yet (e.g. reconnecting without a session cookie)

        var sessionInfo = await Accounts.GetSessionInfo(session, cancellationToken).ConfigureAwait(false);
        if (sessionInfo != null && SystemNow - sessionInfo.LastSeenAt > SessionUpdatePeriod) {
            var upsertSessionCmd = new SessionsBackend_Upsert(session);
            await Commander.Call(upsertSessionCmd, true, cancellationToken).ConfigureAwait(false);
        }

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!account.IsActive())
            return;

        var backendCommand = new UserPresencesBackend_CheckIn(account.Id, SystemNow, isActive);
        _ = Commander.Run(backendCommand, true, CancellationToken.None);
    }
}
