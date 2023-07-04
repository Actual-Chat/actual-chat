namespace ActualChat.Transcription;

public interface ITranscriptStreamer
{
    public IAsyncEnumerable<TranscriptDiff> GetTranscriptDiffStream(Symbol streamId, CancellationToken cancellationToken);
}
