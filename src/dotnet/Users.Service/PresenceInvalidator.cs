namespace ActualChat.Users;

public class PresenceInvalidator : IAsyncDisposable
{
    private readonly CheckIns _checkIns = new ();
    private readonly ConcurrentTimerSet<UserId> _awayTimers;
    private readonly ConcurrentTimerSet<UserId> _offlineTimers;
    private MomentClockSet Clocks { get; }

    private Moment Now => Clocks.SystemClock.Now;

    public PresenceInvalidator(
        Action<UserId> callback,
        MomentClockSet clocks)
    {
        Clocks = clocks;
        _awayTimers = new (ConcurrentTimerSetOptions.Default, callback);
        _offlineTimers = new (ConcurrentTimerSetOptions.Default, callback);
    }

    public async ValueTask DisposeAsync()
    {
        var t1 = _awayTimers.DisposeAsync();
        var t2 = _offlineTimers.DisposeAsync();
        await t1.ConfigureAwait(false);
        await t2.ConfigureAwait(false);
    }

    public Presence GetPresence(UserId userId)
        => ToPresence(_checkIns.Get(userId));

    public bool HandleCheckIn(UserId userId, Moment at)
    {
        var prev = GetPresence(userId);
        var mustInvalidate = prev != ToPresence(at);
        _checkIns.Set(userId, at);
        _awayTimers.AddOrUpdateToLater(userId, at + Constants.Presence.AwayTimeout);
        _offlineTimers.AddOrUpdateToLater(userId, at + Constants.Presence.OfflineTimeout);
        return mustInvalidate;
    }

    private Presence ToPresence(Moment? lastCheckInAt)
    {
        if (lastCheckInAt == null)
            return Presence.Unknown;

        var now = Now;
        if (now - lastCheckInAt < Constants.Presence.AwayTimeout)
            return Presence.Online;

        if (now - lastCheckInAt < Constants.Presence.OfflineTimeout)
            return Presence.Away;

        return Presence.Offline;
    }
}
