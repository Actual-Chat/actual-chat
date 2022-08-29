using ActualChat.Transcription;

namespace ActualChat.Audio;

public interface ITranscriptStreamServer
{
    Task<Option<IAsyncEnumerable<Transcript>>> Read(
        Symbol streamId,
        CancellationToken cancellationToken);

    Task<Task> StartWrite(
        Symbol streamId,
        IAsyncEnumerable<Transcript> transcriptStream,
        CancellationToken cancellationToken);
}
