namespace ActualChat.Live;

/// <summary>
/// Announces the start of a new audio stream within the multiplexed live stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class LiveStreamStart : LiveStreamItem
{
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public LiveStreamInfo StreamInfo { get; init; } = null!;
}
