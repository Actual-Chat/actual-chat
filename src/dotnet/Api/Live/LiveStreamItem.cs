using ActualChat.Async;
using ActualLab.Rpc;

namespace ActualChat.Live;

/// <summary>
/// Base type for all live stream items, enabling polymorphic serialization over multiplexed streams.
/// </summary>
[RpcSerializable]
[DataContract, MemoryPackable, MessagePackObject(true)]
[MemoryPackUnion(0, typeof(LiveStreamStart))]
[MemoryPackUnion(1, typeof(LiveStreamEnd))]
[MemoryPackUnion(2, typeof(LiveAudioFrame))]
[Union(0, typeof(LiveStreamStart))]
[Union(1, typeof(LiveStreamEnd))]
[Union(2, typeof(LiveAudioFrame))]
public abstract partial class LiveStreamItem : IMuxable
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int StreamIndex { get; set; }
}
