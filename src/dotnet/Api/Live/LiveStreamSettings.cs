using MemoryPack;

namespace ActualChat.Live;

/// <summary>
/// Flags indicating the types of live stream content.
/// </summary>
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
