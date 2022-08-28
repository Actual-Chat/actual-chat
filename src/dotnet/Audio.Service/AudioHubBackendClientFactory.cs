using ActualChat.Pooling;

namespace ActualChat.Audio;

public class AudioHubBackendClientFactory
{
    private readonly SharedResourcePool<ClientKey, AudioHubBackendClient> _clientsPool;
    private IServiceProvider Services { get; }

    public AudioHubBackendClientFactory(IServiceProvider services)
    {
        Services = services;
        _clientsPool = new SharedResourcePool<ClientKey, AudioHubBackendClient>(CreateAudioHubBackendClient) {
            ResourceDisposeDelay = TimeSpan.FromMinutes(5),
        };
    }

    private Task<AudioHubBackendClient> CreateAudioHubBackendClient(ClientKey key, CancellationToken cancellationToken)
    {
        var client = new AudioHubBackendClient(key.Address, key.Port, Services);
        return Task.FromResult(client);
    }

    public async Task<IAudioStreamClient> GetAudioStreamClient(
        string address,
        int port,
        CancellationToken cancellationToken)
    {
        var lease = await _clientsPool.Rent(new ClientKey(address, port), cancellationToken).ConfigureAwait(false);
        return new AudioStreamServerReplica(lease);
    }

    public async Task<ITranscriptStreamClient> GetTranscriptStreamClient(
        string address,
        int port,
        CancellationToken cancellationToken)
    {
        var lease = await _clientsPool.Rent(new ClientKey(address, port), cancellationToken).ConfigureAwait(false);
        return new TranscriptStreamServerReplica(lease);
    }

    public record ClientKey(string Address, int Port);
}
