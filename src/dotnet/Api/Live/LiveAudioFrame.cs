using MemoryPack;

namespace ActualChat.Live;

/// <summary>
/// Audio data packet in a multiplexed Live stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LiveAudioFrame : LiveItem
{
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public byte[] Data { get; init; } = [];
}
