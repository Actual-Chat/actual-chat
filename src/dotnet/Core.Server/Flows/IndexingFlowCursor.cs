using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record IndexingFlowCursor<TId>(
    [property: DataMember, MemoryPackOrder(0)] TId LastUpdatedId,
    [property: DataMember, MemoryPackOrder(1)] long LastUpdatedVersion) where TId : ISymbolIdentifier;
