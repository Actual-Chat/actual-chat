using ActualChat.Pooling;

namespace ActualChat.Audio;

public class AudioStreamServerReplica : IAudioStreamClient
{
    private SharedResourcePool<AudioHubBackendClientFactory.ClientKey, AudioHubBackendClient>.Lease Lease { get; }

    internal AudioStreamServerReplica(SharedResourcePool<AudioHubBackendClientFactory.ClientKey, AudioHubBackendClient>.Lease lease)
        => Lease = lease;

    public Task<Option<IAsyncEnumerable<byte[]>>> Read(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken)
        => Lease.Resource.Read(streamId, skipTo, cancellationToken);

    public Task Write(Symbol streamId, IAsyncEnumerable<byte[]> audioStream, CancellationToken cancellationToken)
        => Lease.Resource.Write(streamId, audioStream, cancellationToken);

    public void Dispose()
        => Lease.Dispose();
}
