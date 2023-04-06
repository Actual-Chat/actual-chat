namespace ActualChat.Users;

public sealed class SortedCheckIns
{
    private readonly object _lock = new ();
    // not just queue to avoid duplicates in case of frequent check-ins
    // sorted because we always want to take the earliest one
    private readonly SortedSet<UserCheckIn> _sortedItems = new (CheckInComparer.Default);

    public UserCheckIn? Set(UserId userId, Moment lastCheckInAt)
    {
        lock (_lock) {
            var item = new UserCheckIn(userId, lastCheckInAt);
            if (_sortedItems.TryGetValue(item, out var previous))
                _sortedItems.Remove(item); // force put into the end
            _sortedItems.Add(item);
            return previous;
        }
    }

    public IReadOnlyList<UserCheckIn> PopRange(Moment max)
    {
        lock (_lock) {
            var view = _sortedItems.GetViewBetween(null, new UserCheckIn(UserId.None, max));
            var checkIns = view.ToList();
            view.Clear();
            return checkIns;
        }
    }

    private class CheckInComparer : IComparer<UserCheckIn>
    {
        public static readonly CheckInComparer Default = new ();

        public int Compare(UserCheckIn? x, UserCheckIn? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (ReferenceEquals(null, y)) return 1;
            if (ReferenceEquals(null, x)) return -1;

            var userIdComparison = x.UserId.CompareTo(y.UserId);
            // if same user we count as equal
            if (userIdComparison == 0)
                return 0;

            var lastCheckInAtComparison = x.At.CompareTo(y.At);
            if (lastCheckInAtComparison != 0)
                return lastCheckInAtComparison;

            // if check in times are equal we order by userId cause we need both items
            return userIdComparison;
        }
    }
}
