using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Contacts;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ExternalContact(
    [property: DataMember, MemoryPackOrder(0)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0) : IHasId<ExternalContactId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public string DisplayName { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public string GivenName { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public string FamilyName { get; init; } = "";
    [DataMember, MemoryPackOrder(5)] public string MiddleName { get; init; } = "";
    [DataMember, MemoryPackOrder(6)] public string NamePrefix { get; init; } = "";
    [DataMember, MemoryPackOrder(7)] public string NameSuffix { get; init; } = "";
    [DataMember, MemoryPackOrder(8)] public ApiSet<string> PhoneHashes { get; init; } = ApiSet<string>.Empty;
    [DataMember, MemoryPackOrder(9)] public ApiSet<string> EmailHashes { get; init; } = ApiSet<string>.Empty;
    [DataMember, MemoryPackOrder(10)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(11)] public Moment ModifiedAt { get; init; }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ExternalContactDiff : RecordDiff
{
    public static readonly ExternalContactDiff Empty = new ();
    [DataMember, MemoryPackOrder(0)] public string? DisplayName { get; init; }
    [DataMember, MemoryPackOrder(1)] public string? GivenName { get; init; }
    [DataMember, MemoryPackOrder(2)] public string? FamilyName { get; init; }
    [DataMember, MemoryPackOrder(3)] public string? MiddleName { get; init; }
    [DataMember, MemoryPackOrder(4)] public string? NamePrefix { get; init; }
    [DataMember, MemoryPackOrder(5)] public string? NameSuffix { get; init; }
    [Obsolete("2023.11: Replaced with PhoneHashes")]
    [DataMember, MemoryPackOrder(6)] public SetDiff<ApiSet<Phone>, Phone> Phones { get; init; }
    [Obsolete("2023.11: Replaced with EmailHashes")]
    [DataMember, MemoryPackOrder(7)] public SetDiff<ApiSet<string>, string> Emails { get; init; }
    [DataMember, MemoryPackOrder(8)] public SetDiff<ApiSet<string>, string> PhoneHashes { get; init; }
    [DataMember, MemoryPackOrder(9)] public SetDiff<ApiSet<string>, string> EmailHashes { get; init; }
}
