namespace ActualChat.Transcription;

public interface ITranscriptStreamer
{
    public IAsyncEnumerable<Transcript> GetTranscriptDiffStream(Symbol streamId, CancellationToken cancellationToken);
}
