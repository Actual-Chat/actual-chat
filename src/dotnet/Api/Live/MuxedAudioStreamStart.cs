namespace ActualChat.Live;

/// <summary>
/// Announces the start of a new audio stream within the multiplexed live stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial class MuxedAudioStreamStart : MuxedAudioStreamItem
{
    [DataMember(Order = 1), MemoryPackOrder(1), Key(3)]
    public LiveAudioStreamInfo StreamInfo { get; init; } = null!;
    [DataMember(Order = 2), MemoryPackOrder(2), Key(4)]
    public TimeSpan PlaysAt { get; init; }
}
