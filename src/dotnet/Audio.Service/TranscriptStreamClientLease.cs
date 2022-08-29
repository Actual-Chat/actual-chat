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

    public Task<Option<IAsyncEnumerable<Transcript>>> Read(Symbol streamId, CancellationToken cancellationToken)
        => Lease.Resource.Read(streamId, cancellationToken);

    public Task<Task> StartWrite(
        Symbol streamId,
        IAsyncEnumerable<Transcript> transcriptStream,
        CancellationToken cancellationToken)
        => Lease.Resource.StartWrite(streamId, transcriptStream, cancellationToken);
}
