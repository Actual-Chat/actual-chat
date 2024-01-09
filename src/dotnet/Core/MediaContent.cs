using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record MediaContent(
    [property: DataMember, MemoryPackOrder(0)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(1)] string ContentId,
    [property: DataMember, MemoryPackOrder(2)] MediaId ThumbnailMediaId = default,
    [property: DataMember, MemoryPackOrder(3)] string? ThumbnailContentId = null);
