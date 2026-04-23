using ActualChat.Async;
using ActualLab.Rpc;

namespace ActualChat.Live;

/// <summary>
/// Base type for all live stream items, enabling polymorphic serialization over multiplexed streams.
/// </summary>
[RpcSerializable]
[DataContract, MemoryPackable]
[MemoryPackUnion(0, typeof(LiveStreamStart))]
[MemoryPackUnion(1, typeof(LiveStreamEnd))]
[MemoryPackUnion(2, typeof(LiveAudioFrame))]
[MemoryPackUnion(3, typeof(LiveStreamReset))]
[DerivedTypeShape(typeof(LiveStreamStart), Tag = 0)]
[DerivedTypeShape(typeof(LiveStreamEnd), Tag = 1)]
[DerivedTypeShape(typeof(LiveAudioFrame), Tag = 2)]
[DerivedTypeShape(typeof(LiveStreamReset), Tag = 3)]
public abstract partial class LiveStreamItem : IMuxable
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public int StreamIndex { get; set; }
}
