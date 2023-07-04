using ActualChat.Pooling;
using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamClientLease : ITranscriptStreamClient
{
    private SharedResourcePool<AudioHubBackendClientFactory.ClientKey, AudioHubBackendClient>.Lease Lease { get; }

    internal TranscriptStreamClientLease(SharedResourcePool<AudioHubBackendClientFactory.ClientKey, AudioHubBackendClient>.Lease lease)
        => Lease = lease;

    public void Dispose()
        => Lease.Dispose();

    public Task<IAsyncEnumerable<TranscriptDiff>> Read(Symbol streamId, CancellationToken cancellationToken)
        => Lease.Resource.Read(streamId, cancellationToken);

    public Task Write(Symbol streamId, IAsyncEnumerable<TranscriptDiff> stream, CancellationToken cancellationToken)
        => Lease.Resource.Write(streamId, stream, cancellationToken);
}
