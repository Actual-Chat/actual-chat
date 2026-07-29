using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

/// <summary>
/// Represents a contact imported from the device's address book.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ExternalContact(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version = 0) : IHasId<ExternalContactId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(13), Key(2)] public HashString Hash { get; set; }
}

/// <summary>
/// Extended external contact with full name components and contact info hashes.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ExternalContactFull(ExternalContactId Id, long Version = 0) : ExternalContact(Id, Version)
{
    [DataMember, MemoryPackOrder(2), Key(5)] public string DisplayName { get; init; } = "";
    [DataMember, MemoryPackOrder(3), Key(6)] public string GivenName { get; init; } = "";
    [DataMember, MemoryPackOrder(4), Key(7)] public string FamilyName { get; init; } = "";
    [DataMember, MemoryPackOrder(5), Key(8)] public string MiddleName { get; init; } = "";
    [DataMember, MemoryPackOrder(6), Key(9)] public string NamePrefix { get; init; } = "";
    [DataMember, MemoryPackOrder(7), Key(10)] public string NameSuffix { get; init; } = "";
    [DataMember, MemoryPackOrder(8), Key(11)] public ApiSet<string> PhoneHashes { get; init; } = new ApiSet<string>();
    [DataMember, MemoryPackOrder(9), Key(12)] public ApiSet<string> EmailHashes { get; init; } = new ApiSet<string>();
    [DataMember, MemoryPackOrder(10), Key(13)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(11), Key(14)] public Moment ModifiedAt { get; init; }
}
