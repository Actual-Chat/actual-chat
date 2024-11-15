using MemoryPack;

namespace ActualChat.MLSearch.Indexing.ChatContent;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record EntryIndexCursor(
    [property: DataMember, MemoryPackOrder(0)] long LastLid,
    [property: DataMember, MemoryPackOrder(1)] long LastVersion);
