using MemoryPack;
using Stl.Fusion.Blazor;

namespace ActualChat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Picture(
    [property: DataMember, MemoryPackOrder(0)] MediaContent? MediaContent,
    [property: DataMember, MemoryPackOrder(1)] string? ExternalUrl = null);
