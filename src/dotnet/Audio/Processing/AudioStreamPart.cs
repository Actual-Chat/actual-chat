using ActualChat.Audio.WebM;

namespace ActualChat.Audio.Processing;

public record AudioStreamPart(
    int Index,
    StreamId StreamId,
    AudioRecord AudioRecord,
    WebMDocument Document,
    IReadOnlyList<AudioMetadataEntry> Metadata, // TODO(AY): Discuss the purpose / type of this
    double Offset,
    double Duration);
