namespace ActualChat.Video;

[DataContract, MessagePackObject]
public sealed partial record VideoStreamInfo(
    [property: DataMember, Key(0)] StreamId StreamId,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] AuthorId AuthorId,
    // Top-tier (highest LayerId) format for the simulcast ladder. The
    // per-layer breakdown is derivable from `SourceKind` via the
    // recorder's ladder builder, so we don't carry it per-stream.
    [property: DataMember, Key(3)] VideoFormat Format,
    [property: DataMember, Key(4)] Moment StartedAt,
    [property: DataMember, Key(5)] VideoSourceKind SourceKind = VideoSourceKind.Camera,
    // Source's claimed wall-clock at stream start, never overridden by the server.
    // Used by the client A/V catch-up policy. Falls back to StartedAt when default.
    [property: DataMember, Key(6)] Moment SourceStartedAt = default,
    // Server time minus SourceStartedAt, measured once at registration; includes
    // the PushStream upload leg, so it's an upper bound. The only real skew
    // signal: StartedAt equals SourceStartedAt unless the server's gross-skew
    // guard trips. Null (not 0) when the registering server didn't measure it.
    [property: DataMember, Key(7)] double? SourceClockDeltaMs = null
);
