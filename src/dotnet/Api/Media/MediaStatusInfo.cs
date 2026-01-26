using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Media;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MediaStatusInfo(
    [property: DataMember, MemoryPackOrder(0)] MediaId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version,
    [property: DataMember, MemoryPackOrder(2)] MediaStage Stage,
    [property: DataMember, MemoryPackOrder(3)] double StageProgress,
    [property: DataMember, MemoryPackOrder(4)] string ErrorMessage
) : IHasId<MediaId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public bool HasFailed = !ErrorMessage.IsNullOrEmpty();

    // This record relies on referential equality
    public bool Equals(MediaStatusInfo? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
