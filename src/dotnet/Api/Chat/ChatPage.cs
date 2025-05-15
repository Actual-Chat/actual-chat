using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatPage(
    [property: DataMember, MemoryPackOrder(0)] Range<long>[] EntryIdTileRanges,
    [property: DataMember, MemoryPackOrder(1)] Range<long>[] ConversationIdTileRanges);
