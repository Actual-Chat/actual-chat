using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserAppSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserAppSettings);

    [DataMember, MemoryPackOrder(0)] public bool? IsAnalyticsEnabled{ get; init; }
    [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(2)] public bool? IsExperimentalFeatureEnabled{ get; init; }
    [DataMember, MemoryPackOrder(3)] public bool? IsIncompleteUIEnabled{ get; init; }
}
