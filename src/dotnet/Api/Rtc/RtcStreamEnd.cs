using MemoryPack;

namespace ActualChat.Rtc;

/// <summary>
/// Signals that an audio stream has completed within the multiplexed RTC stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RtcStreamEnd : RtcItem
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int StreamIndex { get; init; }
}
