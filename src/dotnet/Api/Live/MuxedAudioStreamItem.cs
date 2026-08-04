using ActualChat.Async;
using ActualLab.Rpc;

namespace ActualChat.Live;

/// <summary>
/// Base type for all live stream items, enabling polymorphic serialization over multiplexed streams.
/// </summary>
[RpcSerializable]
[DataContract, MessagePackObject]
[Union(0, typeof(MuxedAudioStreamStart))]
[Union(1, typeof(MuxedAudioStreamEnd))]
[Union(2, typeof(MuxedAudioFrame))]
[Union(3, typeof(MuxedAudioStreamReset))]
public abstract partial class MuxedAudioStreamItem : IMuxable
{
    [DataMember(Order = 0), Key(0)]
    public int StreamIndex { get; set; }
}
