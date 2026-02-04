using MemoryPack;

namespace ActualChat.Live;

/// <summary>
/// Announces the start of a new audio stream within the multiplexed Live stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LiveStreamStart : LiveItem
{
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public LiveStreamInfo StreamInfo { get; init; } = null!;
}
