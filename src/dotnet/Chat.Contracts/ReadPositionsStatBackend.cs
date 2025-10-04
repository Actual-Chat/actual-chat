using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ReadPositionsStatBackend(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long StartTrackingEntryLid,
    [property: DataMember, MemoryPackOrder(2)] UserReadPosition[] TopReadPositions);

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial record struct UserReadPosition(
    [property: DataMember, MemoryPackOrder(0)] UserId? UserId,
    [property: DataMember, MemoryPackOrder(1)] long EntryLid)
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
