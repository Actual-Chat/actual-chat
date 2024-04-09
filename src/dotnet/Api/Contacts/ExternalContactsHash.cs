using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Contacts;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ExternalContactsHash(
    [property: DataMember, MemoryPackOrder(0)] UserDeviceId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0)
    : IHasId<UserDeviceId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public string Sha256Hash { get; set; } = "";
    [DataMember, MemoryPackOrder(3)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public Moment ModifiedAt { get; init; }
}
