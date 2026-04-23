namespace ActualChat.Live;

/// <summary>
/// Announces the start of a new audio stream within the multiplexed live stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LiveStreamStart : LiveStreamItem
{
    [DataMember(Order = 1), MemoryPackOrder(1), Key(3)]
    public LiveStreamInfo StreamInfo { get; init; } = null!;
    [DataMember(Order = 2), MemoryPackOrder(2), Key(4)]
    public TimeSpan PlaysAt { get; init; }
}
