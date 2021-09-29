using System.Collections.Generic;
using ActualChat.Audio.WebM;
using ActualChat.Streaming;

namespace ActualChat.Audio.Orchestration
{
    public record AudioStreamPart(
        int Index,
        StreamId StreamId,
        AudioRecord AudioRecord,
        WebMDocument Document,
        IReadOnlyList<AudioMetadataEntry> Metadata, // TODO(AY): Discuss the purpose / type of this
        double Offset,
        double Duration);
}
