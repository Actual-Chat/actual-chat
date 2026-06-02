using ActualChat.Async;
using ActualLab.Rpc;

namespace ActualChat.Live;

/// <summary>
/// Base type for all live stream items, enabling polymorphic serialization over multiplexed streams.
/// </summary>
[RpcSerializable]
[DataContract, MemoryPackable, MessagePackObject]
[MemoryPackUnion(0, typeof(MuxedAudioStreamStart))]
[MemoryPackUnion(1, typeof(MuxedAudioStreamEnd))]
[MemoryPackUnion(2, typeof(MuxedAudioFrame))]
[MemoryPackUnion(3, typeof(MuxedAudioStreamReset))]
[Union(0, typeof(MuxedAudioStreamStart))]
[Union(1, typeof(MuxedAudioStreamEnd))]
[Union(2, typeof(MuxedAudioFrame))]
[Union(3, typeof(MuxedAudioStreamReset))]
public abstract partial class MuxedStreamItem : IMuxable
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public int StreamIndex { get; set; }
}
