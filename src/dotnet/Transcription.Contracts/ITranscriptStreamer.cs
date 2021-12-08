namespace ActualChat.Transcription;

public interface ITranscriptStreamer
{
    public IAsyncEnumerable<TranscriptUpdate> GetTranscriptStream(string streamId, CancellationToken cancellationToken);
}
