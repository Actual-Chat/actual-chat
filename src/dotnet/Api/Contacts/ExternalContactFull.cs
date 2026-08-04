using ActualChat.Hashing;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

/// <summary>
/// Represents a contact imported from the device's address book.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public partial record ExternalContact(
    [property: DataMember, Key(0)] ExternalContactId Id,
    [property: DataMember, Key(1)] long Version = 0) : IHasId<ExternalContactId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public HashString Hash { get; set; }
}

/// <summary>
/// Extended external contact with full name components and contact info hashes.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public partial record ExternalContactFull(ExternalContactId Id, long Version = 0) : ExternalContact(Id, Version)
{
    [DataMember, Key(5)] public string DisplayName { get; init; } = "";
    [DataMember, Key(6)] public string GivenName { get; init; } = "";
    [DataMember, Key(7)] public string FamilyName { get; init; } = "";
    [DataMember, Key(8)] public string MiddleName { get; init; } = "";
    [DataMember, Key(9)] public string NamePrefix { get; init; } = "";
    [DataMember, Key(10)] public string NameSuffix { get; init; } = "";
    [DataMember, Key(11)] public ApiSet<string> PhoneHashes { get; init; } = new ApiSet<string>();
    [DataMember, Key(12)] public ApiSet<string> EmailHashes { get; init; } = new ApiSet<string>();
    [DataMember, Key(13)] public Moment CreatedAt { get; init; }
    [DataMember, Key(14)] public Moment ModifiedAt { get; init; }
}
