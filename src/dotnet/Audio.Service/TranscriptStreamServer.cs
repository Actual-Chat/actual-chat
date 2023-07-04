using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamServer : StreamServerBase<TranscriptDiff>, ITranscriptStreamServer
{
    public TranscriptStreamServer(IServiceProvider services) : base(services)
        => StreamBufferSize = 16;

    public new Task<IAsyncEnumerable<TranscriptDiff>> Read(Symbol streamId, CancellationToken cancellationToken)
        => base.Read(streamId, cancellationToken);

    public new Task Write(Symbol streamId, IAsyncEnumerable<TranscriptDiff> stream, CancellationToken cancellationToken)
        => base.Write(streamId, stream, cancellationToken);
}
