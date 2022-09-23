using ActualChat.Transcription;

namespace ActualChat.Audio;

public interface ITranscriptStreamServer
{
    Task<IAsyncEnumerable<Transcript>> Read(
        Symbol streamId,
        CancellationToken cancellationToken);

    Task Write(
        Symbol streamId,
        IAsyncEnumerable<Transcript> stream,
        CancellationToken cancellationToken);
}
