using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

/// <summary>
/// Stores the combined hash of all external contacts for a user device.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record ExternalContactsHash(
    [property: DataMember, Key(0)] UserDeviceId Id,
    [property: DataMember, Key(1)] long Version = 0)
    : IHasId<UserDeviceId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(4)] public HashString Hash { get; set; }
    [DataMember, Key(2)] public Moment CreatedAt { get; init; }
    [DataMember, Key(3)] public Moment ModifiedAt { get; init; }
}
