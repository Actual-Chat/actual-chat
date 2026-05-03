using ActualChat.Video;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record VideoStreamInfo(
    [property: DataMember, MemoryPackOrder(0), Key(0)] StreamId StreamId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(3), Key(3)] VideoFormat[] Formats, // Per each SpacialLayerId
    [property: DataMember, MemoryPackOrder(4), Key(4)] Moment StartedAt,
    [property: DataMember, MemoryPackOrder(5), Key(5)] StreamKind StreamKind = StreamKind.Webcam,
    // Source's claimed wall-clock at stream start, never overridden by the server.
    // Used by the client A/V catch-up policy. Falls back to StartedAt when default.
    [property: DataMember, MemoryPackOrder(6), Key(6)] Moment SourceStartedAt = default
);
