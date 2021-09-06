using System.Collections.Generic;
using ActualChat.Audio.WebM;
using ActualChat.Distribution;
using Stl.Text;

namespace ActualChat.Audio.Orchestration
{
    public record AudioEntry(
        int Index,
        StreamId StreamId,
        AudioRecording Recording,
        WebMDocument Document,
        IReadOnlyList<AudioMetaDataEntry> MetaData,
        double Offset,
        double Duration);
}