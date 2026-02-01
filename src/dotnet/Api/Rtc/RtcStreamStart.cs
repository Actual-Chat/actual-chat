using ActualChat.Audio;
using MemoryPack;

namespace ActualChat.Rtc;

/// <summary>
/// Announces the start of a new audio stream within the multiplexed RTC stream.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RtcStreamStart : RtcItem
{
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public Moment BeginsAt { get; init; }

    [DataMember(Order = 2), MemoryPackOrder(2)]
    public AuthorId? AuthorId { get; init; }

    [DataMember(Order = 3), MemoryPackOrder(3)]
    public ChatEntryId? EntryId { get; init; }

    [DataMember(Order = 4), MemoryPackOrder(4)]
    public AudioFormat? Format { get; init; }
}
