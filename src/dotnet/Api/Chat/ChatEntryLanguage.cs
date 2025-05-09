using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ChatEntryLanguage(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long Version = 0
    ): IHasId<ChatEntryId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public Language[] Languages { get; init; } = [];
    [DataMember, MemoryPackOrder(3)] public DateTime CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public DateTime ModifiedAt { get; init; }

    // This record relies on referential equality
    public bool Equals(ChatEntryLanguage? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
