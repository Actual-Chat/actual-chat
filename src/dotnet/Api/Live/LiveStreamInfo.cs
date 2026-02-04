using ActualChat.Audio;
using MemoryPack;

namespace ActualChat.Live;

/// <summary>
/// Information about an active audio stream in a chat.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LiveStreamInfo
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public AuthorId AuthorId { get; init; } = null!;
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public string StreamId { get; init; } = "";
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public Moment BeginsAt { get; init; }
    [DataMember(Order = 4), MemoryPackOrder(4)]
    public AudioFormat? Format { get; init; }
}
