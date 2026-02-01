using MemoryPack;

namespace ActualChat.Rtc;

/// <summary>
/// Base type for all RTC stream items, enabling polymorphic serialization over multiplexed streams.
/// </summary>
[DataContract, MemoryPackable]
[MemoryPackUnion(0, typeof(RtcAudioFrame))]
[MemoryPackUnion(1, typeof(RtcStreamStart))]
[MemoryPackUnion(2, typeof(RtcStreamEnd))]
public abstract partial class RtcItem;
