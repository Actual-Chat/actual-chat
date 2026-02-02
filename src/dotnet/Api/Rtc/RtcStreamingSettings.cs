using MemoryPack;

namespace ActualChat.Rtc;

[Flags]
public enum RtcStreamKind
{
    None = 0,
    Audio = 1,
}

/// <summary>
/// Configuration for an RTC stream subscription.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record RtcStreamingSettings
{
    public static readonly RtcStreamingSettings Default = new();

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public RtcStreamKind StreamKindFilter { get; init; } = RtcStreamKind.Audio;
}
