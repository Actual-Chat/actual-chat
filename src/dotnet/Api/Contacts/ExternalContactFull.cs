using ActualChat.Hashing;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ExternalContact(
    [property: DataMember, MemoryPackOrder(0)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0) : IHasId<ExternalContactId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(13)] public HashString Hash { get; set; }
}

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ExternalContactFull(ExternalContactId Id, long Version = 0) : ExternalContact(Id, Version)
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
