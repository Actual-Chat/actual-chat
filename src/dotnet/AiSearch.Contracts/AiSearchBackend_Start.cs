using MemoryPack;

namespace ActualChat.AiSearch;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AiSearchBackend_Start(
);
