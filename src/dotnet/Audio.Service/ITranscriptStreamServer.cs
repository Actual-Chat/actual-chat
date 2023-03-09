using ActualChat.Transcription;

namespace ActualChat.Audio;

public interface ITranscriptStreamServer
{
    Task<IAsyncEnumerable<TranscriptDiff>> Read(
        Symbol streamId,
        CancellationToken cancellationToken);

    Task Write(
        Symbol streamId,
        IAsyncEnumerable<TranscriptDiff> stream,
        CancellationToken cancellationToken);
}
