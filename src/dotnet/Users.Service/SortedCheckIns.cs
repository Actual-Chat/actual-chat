namespace ActualChat.Users;

public sealed class SortedCheckIns
{
    private static readonly UserId _maxUserId = new (new string('z', 8));
    private readonly object _lock = new ();
    // not just queue to avoid duplicates in case of frequent check-ins
    // sorted because we always want to take the earliest one
    private readonly SortedSet<UserCheckIn> _sortedItems = new (CheckInComparer.Default);
    private readonly Dictionary<UserId, Moment> _items = new ();

    public UserCheckIn? Set(UserCheckIn checkIn)
    {
        lock (_lock) {
            UserCheckIn? previous = null;
            if (_items.TryGetValue(checkIn.UserId, out var prevAt)) {
                if (prevAt >= checkIn.At)
                    return null;

                previous = checkIn with { At = prevAt };
                _sortedItems.Remove(previous); // force put into the end
            }
            _items[checkIn.UserId] = checkIn.At;
            _sortedItems.Add(checkIn);
            return previous;
        }
    }

    public IReadOnlyList<UserCheckIn> PopRange(Moment max)
    {
        lock (_lock) {
            var view = _sortedItems.GetViewBetween(null, new UserCheckIn(_maxUserId, max));
            var checkIns = view.ToList();
            if (checkIns.Count > 0) {
                view.Clear();
                foreach (var checkIn in checkIns)
                    _items.Remove(checkIn.UserId);
            }
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
