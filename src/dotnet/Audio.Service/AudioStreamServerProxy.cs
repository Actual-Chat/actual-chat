using ActualChat.Kubernetes;

namespace ActualChat.Audio;

public class AudioStreamServerProxy : IAudioStreamServer
{
    private AudioStreamServer AudioStreamServer { get; }
    private AudioHubBackendClientFactory AudioHubBackendClientFactory { get; }
    private ServiceRegistry ServiceRegistry { get; }
    private AudioSettings Settings { get; }

    public AudioStreamServerProxy(AudioStreamServer audioStreamServer, AudioHubBackendClientFactory audioHubBackendClientFactory, ServiceRegistry serviceRegistry, AudioSettings settings)
    {
        AudioStreamServer = audioStreamServer;
        AudioHubBackendClientFactory = audioHubBackendClientFactory;
        ServiceRegistry = serviceRegistry;
        Settings = settings;
    }

    public async Task<Option<IAsyncEnumerable<byte[]>>> Read(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        if (KubernetesInfo.POD_IP.IsNullOrEmpty() || await KubernetesConfig.IsInCluster(cancellationToken).ConfigureAwait(false))
            return await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);

        var audioStreamOption = await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        if (audioStreamOption.HasValue)
            return audioStreamOption;

        // TODO(AK): use consistent hashing to get service replicas
        var endpointState = await ServiceRegistry.GetServiceEndpoints(Settings.Namespace, Settings.ServiceName, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.LatestNonErrorValue;
        var port = serviceEndpoints.Ports
            .Where(p => string.Equals(p.Name, "http", StringComparison.Ordinal))
            .Select(p => (int?)p.Port)
            .FirstOrDefault();
        var alternateAddress = serviceEndpoints.Endpoints
            .Where(e => e.IsReady)
            .SelectMany(e => e.Addresses)
            .OrderBy(a => a)
            .FirstOrDefault(a => !string.Equals(a, KubernetesInfo.POD_IP, StringComparison.Ordinal));
        if (alternateAddress == null || !port.HasValue)
            return audioStreamOption;

        var client = AudioHubBackendClientFactory.Get(alternateAddress, port.Value);
        return await client.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
    }

    public async Task Write(Symbol streamId, IAsyncEnumerable<byte[]> audioStream, CancellationToken cancellationToken)
    {
        if (KubernetesInfo.POD_IP.IsNullOrEmpty() || await KubernetesConfig.IsInCluster(cancellationToken).ConfigureAwait(false)) {
            await AudioStreamServer.Write(streamId, audioStream, cancellationToken).ConfigureAwait(false);
            return;
        }

        // TODO(AK): use consistent hashing to get service replicas
        var endpointState = await ServiceRegistry.GetServiceEndpoints(Settings.Namespace, Settings.ServiceName, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.LatestNonErrorValue;
        var port = serviceEndpoints.Ports
            .Where(p => string.Equals(p.Name, "http", StringComparison.Ordinal))
            .Select(p => (int?)p.Port)
            .FirstOrDefault();
        var alternateAddresses = serviceEndpoints.Endpoints
            .Where(e => e.IsReady)
            .SelectMany(e => e.Addresses)
            .ToList();
        if (alternateAddresses.Count == 0 || !port.HasValue) {
            await AudioStreamServer.Write(streamId, audioStream, cancellationToken).ConfigureAwait(false);
            return;
        }

        var memoized = audioStream.Memoize(cancellationToken);
        foreach (var alternateAddress in alternateAddresses) {
            var client = AudioHubBackendClientFactory.Get(alternateAddress, port.Value);
            await client.Write(streamId, memoized.Replay(cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        await AudioStreamServer.Write(streamId, memoized.Replay(cancellationToken), cancellationToken).ConfigureAwait(false);
    }
}
