using MemoryPack;

namespace ActualChat.MLSearch;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record MLSearchBackend_Start(
);
