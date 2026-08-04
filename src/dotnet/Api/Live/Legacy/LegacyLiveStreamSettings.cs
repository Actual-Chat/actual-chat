namespace ActualChat.Live;

/// <summary>
/// Flags indicating the types of live stream content.
/// </summary>
[Flags]
public enum LegacyLiveStreamKind
{
    None = 0,
    Audio = 1,
}

/// <summary>
/// Configuration for a live stream subscription.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record LegacyLiveStreamSettings
{
    public static readonly LegacyLiveStreamSettings Default = new();

    [DataMember(Order = 1), Key(0)]
    public LegacyLiveStreamKind StreamKindFilter { get; init; } = LegacyLiveStreamKind.Audio;
}
