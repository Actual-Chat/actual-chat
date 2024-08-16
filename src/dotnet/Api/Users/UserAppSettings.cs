using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserAppSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserAppSettings);

    [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(0)] public bool? IsDataCollectionEnabled{ get; init; }
    [DataMember, MemoryPackOrder(2)] public bool? AreExperimentalFeaturesEnabled{ get; init; }
    [DataMember, MemoryPackOrder(3)] public bool? IsIncompleteUIEnabled{ get; init; }
}
