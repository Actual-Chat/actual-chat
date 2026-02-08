using ActualChat.Async;

namespace ActualChat.Live;

/// <summary>
/// Base type for all live stream items, enabling polymorphic serialization over multiplexed streams.
/// </summary>
[DataContract, MemoryPackable, MessagePackObject(true)]
[MemoryPackUnion(0, typeof(LiveAudioFrame))]
[MemoryPackUnion(1, typeof(LiveStreamStart))]
[MemoryPackUnion(2, typeof(LiveStreamEnd))]
[Union(0, typeof(LiveAudioFrame))]
[Union(1, typeof(LiveStreamStart))]
[Union(2, typeof(LiveStreamEnd))]
public abstract partial class LiveStreamItem : IMuxable
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int StreamIndex { get; set; }
}
