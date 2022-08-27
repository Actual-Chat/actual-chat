using ActualChat.Pooling;
using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamServerReplica : ITranscriptStreamClient
{
    private SharedResourcePool<AudioHubBackendClientFactory.ClientKey, AudioHubBackendClient>.Lease Lease { get; }

    internal TranscriptStreamServerReplica(SharedResourcePool<AudioHubBackendClientFactory.ClientKey, AudioHubBackendClient>.Lease lease)
        => Lease = lease;

    public Task<Option<IAsyncEnumerable<Transcript>>> Read(Symbol streamId, CancellationToken cancellationToken)
        => Lease.Resource.Read(streamId, cancellationToken);

    public Task Write(
        Symbol streamId,
        IAsyncEnumerable<Transcript> transcriptStream,
        CancellationToken cancellationToken)
        => Lease.Resource.Write(streamId, transcriptStream, cancellationToken);

    public void Dispose()
        => Lease.Dispose();
}
