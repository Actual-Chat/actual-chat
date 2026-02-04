using MemoryPack;

namespace ActualChat.Live;

[Flags]
public enum LiveStreamKind
{
    None = 0,
    Audio = 1,
}

/// <summary>
/// Configuration for a Live stream subscription.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LiveStreamingSettings
{
    public static readonly LiveStreamingSettings Default = new();

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public LiveStreamKind StreamKindFilter { get; init; } = LiveStreamKind.Audio;
}
