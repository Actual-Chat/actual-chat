using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Translation(
    [property: DataMember, MemoryPackOrder(0)] TranslationId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
) : IHasId<TranslationId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public Language TargetLanguage { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public string Content { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public HashString SourceContentHash { get; init; }
    [DataMember, MemoryPackOrder(5)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(6)] public Moment ModifiedAt { get; init; }
    [DataMember, MemoryPackOrder(7)] public LinearMap TimeMap { get; set; }
    [DataMember, MemoryPackOrder(8)] public Symbol StreamId { get; set; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsStreaming => !StreamId.IsEmpty;
}
