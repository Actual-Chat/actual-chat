using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatEntryLanguage(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long Version = 0
    ): IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public ApiArray<Language> Languages { get; init; } = ApiArray<Language>.Empty;
    [DataMember, MemoryPackOrder(3)] public DateTime CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public DateTime ModifiedAt { get; init; }
}
