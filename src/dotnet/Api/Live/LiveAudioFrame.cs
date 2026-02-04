using MemoryPack;

namespace ActualChat.Live;

/// <summary>
/// Audio data packet in a multiplexed live stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LiveAudioFrame : LiveStreamItem
{
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public byte[] Data { get; init; } = [];
}
