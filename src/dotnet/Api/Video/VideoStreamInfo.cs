namespace ActualChat.Video;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record VideoStreamInfo(
    [property: DataMember, MemoryPackOrder(0), Key(0)] StreamId StreamId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] AuthorId AuthorId,
    // Top-tier (highest LayerId) format for the simulcast ladder. The
    // per-layer breakdown is derivable from `SourceKind` via the
    // recorder's ladder builder, so we don't carry it per-stream.
    [property: DataMember, MemoryPackOrder(3), Key(3)] VideoFormat Format,
    [property: DataMember, MemoryPackOrder(4), Key(4)] Moment StartedAt,
    [property: DataMember, MemoryPackOrder(5), Key(5)] VideoSourceKind SourceKind = VideoSourceKind.Camera,
    // Source's claimed wall-clock at stream start, never overridden by the server.
    // Used by the client A/V catch-up policy. Falls back to StartedAt when default.
    [property: DataMember, MemoryPackOrder(6), Key(6)] Moment SourceStartedAt = default,
    // Server time minus SourceStartedAt, measured once at registration; includes
    // the PushStream upload leg, so it's an upper bound. The only real skew
    // signal: StartedAt equals SourceStartedAt unless the server's gross-skew
    // guard trips. Null (not 0) when the registering server didn't measure it.
    [property: DataMember, MemoryPackOrder(7), Key(7)] double? SourceClockDeltaMs = null
);
