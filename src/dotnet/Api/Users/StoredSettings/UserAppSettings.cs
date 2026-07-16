using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for application-wide features.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserAppSettings : StoredSettings, IHasOrigin, IHasKvasKey<UserAppSettings>
{
    [DataMember, MemoryPackOrder(1), Key(1)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(0), Key(0)] public bool? IsDataCollectionEnabled{ get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public bool? AreExperimentalFeaturesEnabled{ get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public bool? IsIncompleteUIEnabled{ get; init; }
    // MemoryPackOrder(4) reserved (was IsVideoStreamingEnabled) — do not reuse.
    [DataMember, MemoryPackOrder(5), Key(5)] public bool? IsAuthorColorsEnabled{ get; init; }
}
