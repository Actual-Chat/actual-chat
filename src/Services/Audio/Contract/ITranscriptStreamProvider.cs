using ActualChat.Streaming;

namespace ActualChat.Audio
{
    public interface ITranscriptStreamProvider : IStreamProvider<StreamId, TranscriptPart>
    { }
}
