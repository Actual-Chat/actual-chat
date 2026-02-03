using MemoryPack;

namespace ActualChat.Rtc;

/// <summary>
/// Announces the start of a new audio stream within the multiplexed RTC stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RtcStreamStart : RtcItem
{
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public RtcStreamInfo StreamInfo { get; init; } = null!;
}
