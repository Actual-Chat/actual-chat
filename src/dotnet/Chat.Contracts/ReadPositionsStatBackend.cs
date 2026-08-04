namespace ActualChat.Chat;

/// <summary>
/// Tracks the top read positions for users in a chat.
/// </summary>
[DataContract, MessagePackObject]
public partial record ReadPositionsStatBackend(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long StartTrackingEntryLid,
    [property: DataMember, Key(2)] UserReadPosition[] TopReadPositions);

/// <summary>
/// Represents a user's read position in a chat.
/// </summary>
[DataContract, MessagePackObject]
public readonly partial record struct UserReadPosition(
    [property: DataMember, Key(0)] UserId? UserId,
    [property: DataMember, Key(1)] long EntryLid)
{
    public static IComparer<UserReadPosition> Comparer { get; } = new RelationalComparer();

    // Nested types

    private sealed class RelationalComparer : IComparer<UserReadPosition>
    {
        public int Compare(UserReadPosition x, UserReadPosition y)
        {
            var r = -x.EntryLid.CompareTo(y.EntryLid); // EntryLid in descending order
            if (r != 0) return r;

            var xUserId = x.UserId?.Id ?? Symbol.Empty;
            var yUserId = y.UserId?.Id ?? Symbol.Empty;
            return xUserId.CompareTo(yUserId); // UserIds in ascending order
        }
    }
}
