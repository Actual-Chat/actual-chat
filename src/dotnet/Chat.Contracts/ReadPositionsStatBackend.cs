using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ReadPositionsStatBackend(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long StartTrackingEntryLid,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<UserReadPosition> TopReadPositions);

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record UserReadPosition(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] long EntryLid);
