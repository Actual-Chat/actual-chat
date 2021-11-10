namespace ActualChat.Transcription;

public interface ITranscriptStreamer
{
    public IAsyncEnumerable<TranscriptUpdate> GetTranscriptStream(StreamId streamId, CancellationToken cancellationToken);
}
