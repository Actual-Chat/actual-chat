using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatRangeMeta(
    [property: DataMember, MemoryPackOrder(0)] Range<long> IdRange,
    [property: DataMember, MemoryPackOrder(1)] Range<long>[] EntryIdRanges,
    [property: DataMember, MemoryPackOrder(2)] Range<long>[] ConversationIdRanges,
    [property: DataMember, MemoryPackOrder(3)] int MinCount,
    [property: DataMember, MemoryPackOrder(4)] long? PreviousIdTileStart,
    [property: DataMember, MemoryPackOrder(5)] long? NextIdTileStart);
