using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for replay playback (e.g., speed).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record UserReplaySettings : StoredSettings, IHasOrigin, IHasKvasKey<UserReplaySettings>
{
    [DataMember, MemoryPackOrder(0)]
    public double Speed { get; init; } = 1.0;

    [DataMember, MemoryPackOrder(1)]
    public string Origin { get; init; } = "";
}
