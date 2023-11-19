using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamServer : StreamServerBase<TranscriptDiff>, ITranscriptStreamServer
{
    public TranscriptStreamServer(IServiceProvider services) : base(services)
        => StreamBufferSize = 16;

    public new virtual Task<IAsyncEnumerable<TranscriptDiff>> Read(Symbol streamId, CancellationToken cancellationToken)
        => base.Read(streamId, cancellationToken);

    public new virtual Task Write(Symbol streamId, IAsyncEnumerable<TranscriptDiff> stream, CancellationToken cancellationToken)
        => base.Write(streamId, stream, cancellationToken);

    public TranscriptStreamServer SkipDispose()
        => new SkipDisposeWrapper(this);

    private sealed class SkipDisposeWrapper(TranscriptStreamServer instance) : TranscriptStreamServer(instance.Services)
    {
        public override Task<IAsyncEnumerable<TranscriptDiff>> Read(Symbol streamId, CancellationToken cancellationToken)
            => instance.Read(streamId, cancellationToken);

        public override Task Write(Symbol streamId, IAsyncEnumerable<TranscriptDiff> stream, CancellationToken cancellationToken)
            => instance.Write(streamId, stream, cancellationToken);

#pragma warning disable CA2215
        public override void Dispose()
        { }
#pragma warning restore CA2215
    }
}
