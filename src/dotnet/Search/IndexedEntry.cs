using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Search;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class IndexedEntry
{
    [DataMember, MemoryPackOrder(0)] public long Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Content { get; init; } = "";
}
