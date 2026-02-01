using MemoryPack;

namespace ActualChat.Rtc;

/// <summary>
/// Configuration for an RTC stream subscription.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record RtcStreamConfig
{
    public static readonly RtcStreamConfig Default = new();

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public bool IsListening { get; init; } = true;

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public RtcStreamKind Kinds { get; init; } = RtcStreamKind.Audio;
}

[Flags]
public enum RtcStreamKind
{
    None = 0,
    Audio = 1,
    // Future: Video = 2,
}
