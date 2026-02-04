using MemoryPack;

namespace ActualChat.Live;

/// <summary>
/// Signals that an audio stream has completed within the multiplexed live stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class LiveStreamEnd : LiveStreamItem;
