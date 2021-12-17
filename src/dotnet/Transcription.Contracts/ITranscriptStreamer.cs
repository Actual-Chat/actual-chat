namespace ActualChat.Transcription;

public interface ITranscriptStreamer
{
    public IAsyncEnumerable<Transcript> GetTranscriptDiffStream(string streamId, CancellationToken cancellationToken);
}
