using ActualChat.Audio;

namespace ActualChat.Live;

/// <summary>
/// Information about an active audio stream in a chat.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record LiveAudioStreamInfo
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public AuthorId AuthorId { get; init; } = null!;
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public string StreamId { get; init; } = "";
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public Moment BeginsAt { get; init; }
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public AudioFormat? Format { get; init; }
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public ChatEntryId? EntryId { get; init; }
    // Source's claimed wall-clock at stream start, never overridden by the server.
    // Used by the client A/V catch-up policy. Falls back to BeginsAt when default.
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)]
    public Moment SourceBeginsAt { get; init; }
    // JustText authors: transcribed, never fanned out. Negative so older entries default to voice.
    [DataMember(Order = 7), MemoryPackOrder(7), Key(7)]
    public bool IsTextOnly { get; init; }
}
