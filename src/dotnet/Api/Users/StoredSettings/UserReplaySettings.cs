using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for replay playback (e.g., speed).
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserReplaySettings : StoredSettings, IHasOrigin, IHasKvasKey<UserReplaySettings>
{
    public static string KvasKey => nameof(UserReplaySettings);

    [DataMember, MemoryPackOrder(0), Key(0)]
    public double Speed { get; init; } = 1.0;
    [DataMember, MemoryPackOrder(1), Key(1)]
    public string Origin { get; init; } = "";
}
