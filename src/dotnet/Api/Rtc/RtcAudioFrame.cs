using MemoryPack;

namespace ActualChat.Rtc;

/// <summary>
/// Audio data packet in a multiplexed RTC stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RtcAudioFrame : RtcItem
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int StreamIndex { get; init; }

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public byte[] Data { get; init; } = [];
}
