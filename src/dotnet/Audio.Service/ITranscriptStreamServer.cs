using ActualChat.Transcription;

namespace ActualChat.Audio;

public interface ITranscriptStreamServer
{
    IAsyncEnumerable<Transcript> Read(
        Symbol streamId,
        CancellationToken cancellationToken);

    Task Write(
        Symbol streamId,
        IAsyncEnumerable<Transcript> transcriptStream,
        CancellationToken cancellationToken);
}
