using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamServer : StreamServerBase<Transcript>, ITranscriptStreamServer
{
    public TranscriptStreamServer(IServiceProvider services) : base(services)
        => StreamBufferSize = 16;

    public new Task<IAsyncEnumerable<Transcript>> Read(Symbol streamId, CancellationToken cancellationToken)
        => base.Read(streamId, cancellationToken);

    public new Task Write(Symbol streamId, IAsyncEnumerable<Transcript> stream, CancellationToken cancellationToken)
        => base.Write(streamId, stream, cancellationToken);
}
