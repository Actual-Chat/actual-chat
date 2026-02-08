using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

/// <summary>
/// Stores the combined hash of all external contacts for a user device.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record ExternalContactsHash(
    [property: DataMember, MemoryPackOrder(0)] UserDeviceId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0)
    : IHasId<UserDeviceId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(5)] public HashString Hash { get; set; }
    [DataMember, MemoryPackOrder(3)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public Moment ModifiedAt { get; init; }
}
