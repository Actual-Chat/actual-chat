namespace ActualChat.Users.Internal;

public sealed class UserPresence(UserId userId)
{
    private readonly Lock _lock = new();
    private CheckIn? _lastCheckIn;

    public UserId UserId { get; } = userId;

    public Presence GetPresence(Moment now)
    {
        lock (_lock)
            return ToPresence(_lastCheckIn, now);
    }

    public Moment? LastActiveAt {
        get {
            lock (_lock) {
                var ci = _lastCheckIn;
                return ci?.LastActiveAt ?? ci?.At;
            }
        }
    }

    public (Presence Old, Presence New) CheckIn(Moment at, bool isActive, Moment now)
    {
        lock (_lock) {
            var prev = _lastCheckIn;
            var oldPresence = ToPresence(prev, now);
            var newCheckIn = new CheckIn(at, isActive, prev);
            var newPresence = ToPresence(newCheckIn, now);
            _lastCheckIn = newPresence is Presence.Online or Presence.Away ? newCheckIn : null;
            return (oldPresence, newPresence);
        }
    }

    public bool TryGoOffline()
    {
        lock (_lock) {
            if (_lastCheckIn is null)
                return false;
            _lastCheckIn = null;
            return true;
        }
    }

    // Private methods

    private static Presence ToPresence(CheckIn? lastCheckIn, Moment now)
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
