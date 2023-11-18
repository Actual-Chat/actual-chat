using ActualChat.Pooling;

namespace ActualChat.Audio;

public sealed class AudioStreamClientLease : IAudioStreamClient
{
    private SharedResourcePool<AudioHubBackendClientFactory.ClientKey, AudioHubBackendClient>.Lease Lease { get; }

    internal AudioStreamClientLease(SharedResourcePool<AudioHubBackendClientFactory.ClientKey, AudioHubBackendClient>.Lease lease)
        => Lease = lease;

    public void Dispose()
        => Lease.Dispose();

    public Task<IAsyncEnumerable<byte[]>> Read(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken)
        => Lease.Resource.Read(streamId, skipTo, cancellationToken);

    public Task Write(Symbol streamId, IAsyncEnumerable<byte[]> stream, CancellationToken cancellationToken)
        => Lease.Resource.Write(streamId, stream, cancellationToken);
}
