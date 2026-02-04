using MemoryPack;

namespace ActualChat.Live;

[Flags]
public enum LiveStreamKind
{
    None = 0,
    Audio = 1,
}

/// <summary>
/// Configuration for a live stream subscription.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LiveStreamSettings
{
    public static readonly LiveStreamSettings Default = new();

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public LiveStreamKind StreamKindFilter { get; init; } = LiveStreamKind.Audio;
}
