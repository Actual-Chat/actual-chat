namespace ActualChat.Live;

/// <summary>
/// Signals that the multiplexed live stream has been reset due to a reconnect.
/// All in-flight sub-streams should be flushed and re-started.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial class MuxedAudioStreamReset : MuxedAudioStreamItem;
