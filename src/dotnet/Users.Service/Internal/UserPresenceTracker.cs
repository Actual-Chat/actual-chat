namespace ActualChat.Users.Internal;

internal class UserPresenceTracker : IAsyncDisposable
{
    private readonly CheckInTracker _checkIns = new();
    private readonly ConcurrentTimerSet<UserId> _awayTimers;
    private readonly ConcurrentTimerSet<UserId> _offlineTimers;
    private readonly Action<UserId> _onPresenceChanged;

    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public UserPresenceTracker(Action<UserId> onPresenceChanged, MomentClockSet clocks)
    {
        _onPresenceChanged = onPresenceChanged;
        Clocks = clocks;
        _awayTimers = new (ConcurrentTimerSetOptions.Default, OnAway);
        _offlineTimers = new (ConcurrentTimerSetOptions.Default, OnOffline);
    }

    public async ValueTask DisposeAsync()
    {
        var t1 = _awayTimers.DisposeAsync();
        var t2 = _offlineTimers.DisposeAsync();
        await t1.ConfigureAwait(false);
        await t2.ConfigureAwait(false);
    }

    public Presence GetPresence(UserId userId)
        => ToPresence(_checkIns.Get(userId), Now);

    public void CheckIn(UserId userId, Moment at, bool isActive)
    {
        var now = Now;
        var lastCheckIn = _checkIns.Get(userId);
        var oldPresence = ToPresence(lastCheckIn, now);
        var newPresence = ToPresence(new (at, isActive, lastCheckIn), now);
        if (newPresence is Presence.Online or Presence.Away) {
            _checkIns.Set(userId, at, isActive);
            if (newPresence is Presence.Online)
                _awayTimers.AddOrUpdateToLater(userId, at + Constants.Presence.AwayTimeout);
            _offlineTimers.AddOrUpdateToLater(userId, at + Constants.Presence.OfflineTimeout);
        }
        else {
            _checkIns.Remove(userId);
            _awayTimers.Remove(userId);
            _offlineTimers.Remove(userId);
        }
        if (oldPresence != newPresence)
            _onPresenceChanged.Invoke(userId);
    }

    public void OnAway(UserId userId)
        => _onPresenceChanged.Invoke(userId);

    public void OnOffline(UserId userId)
    {
        _checkIns.Remove(userId);
        _onPresenceChanged.Invoke(userId);
    }

    private Presence ToPresence(CheckIn? lastCheckIn, Moment now)
    {
        if (lastCheckIn == null)
            return Presence.Offline;

        var activityRecency = now - lastCheckIn.LastActiveAt;
        if (activityRecency < Constants.Presence.AwayTimeout)
            return Presence.Online;

        var checkInRecency = now - lastCheckIn.At;
        if (checkInRecency < Constants.Presence.OfflineTimeout)
            return Presence.Away;

        return Presence.Offline;
    }
}
