using ActualChat.Kubernetes;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Audio;

public class AudioStreamServerProxy : IAudioStreamServer
{
    private AudioStreamServer AudioStreamServer { get; }
    private AudioHubBackendClientFactory AudioHubBackendClientFactory { get; }
    private ServiceRegistry ServiceRegistry { get; }
    private AudioSettings Settings { get; }
    private ILogger<AudioStreamServerProxy> Log { get; }

    public AudioStreamServerProxy(
        AudioStreamServer audioStreamServer,
        AudioHubBackendClientFactory audioHubBackendClientFactory,
        ServiceRegistry serviceRegistry,
        AudioSettings settings,
        ILogger<AudioStreamServerProxy> log)
    {
        AudioStreamServer = audioStreamServer;
        AudioHubBackendClientFactory = audioHubBackendClientFactory;
        ServiceRegistry = serviceRegistry;
        Settings = settings;
        Log = log;
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
        var alternateAddresses = serviceEndpoints.Endpoints
            .Where(e => e.IsReady)
            .SelectMany(e => e.Addresses)
            .Where(a => !OrdinalEquals(a, KubernetesInfo.POD_IP))
            .OrderBy(a => a.GetDjb2HashCode() * (long)streamId.HashCode % 33461)
            .ToList();
        if (alternateAddresses.Count == 0 || !port.HasValue)
            return audioStreamOption;

        foreach (var alternateAddress in alternateAddresses) {
            var client = await AudioHubBackendClientFactory.GetAudioStreamClient(alternateAddress, port.Value, cancellationToken).ConfigureAwait(false);
            var streamOption = await client.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
            if (streamOption.HasValue)
                return streamOption;
        }
        return audioStreamOption;
    }

    public async Task<Task> Write(Symbol streamId, IAsyncEnumerable<byte[]> audioStream, CancellationToken cancellationToken)
    {
        if (KubernetesInfo.POD_IP.IsNullOrEmpty()
            || await KubernetesConfig.IsInCluster(cancellationToken).ConfigureAwait(false))
            return await AudioStreamServer.Write(streamId, audioStream, cancellationToken).ConfigureAwait(false);

        // TODO(AK): use consistent hashing to get service replicas
        var endpointState = await ServiceRegistry
            .GetServiceEndpoints(Settings.Namespace, Settings.ServiceName, cancellationToken)
            .ConfigureAwait(false);
        var serviceEndpoints = endpointState.LatestNonErrorValue;
        var port = serviceEndpoints.Ports
            .Where(p => string.Equals(p.Name, "http", StringComparison.Ordinal))
            .Select(p => (int?)p.Port)
            .FirstOrDefault();
        var alternateAddresses = serviceEndpoints.Endpoints
            .Where(e => e.IsReady)
            .SelectMany(e => e.Addresses)
            .ToList();
        if (alternateAddresses.Count == 0 || !port.HasValue)
            return await AudioStreamServer.Write(streamId, audioStream, cancellationToken).ConfigureAwait(false);

        var memoized = audioStream.Memoize(cancellationToken);
        var completeOnSelfTask = AudioStreamServer.Write(streamId, memoized);
        var writeTasks = await alternateAddresses
            .Select(alternateAddress => AudioHubBackendClientFactory.GetAudioStreamClient(alternateAddress, port.Value, cancellationToken))
            .Select(async clientTask => {
                var client = await clientTask.ConfigureAwait(false);
                return client.Write(streamId, memoized.Replay(cancellationToken), cancellationToken);
            })
            .Collect()
            .ConfigureAwait(false);

        if (writeTasks.Count == 0)
            return completeOnSelfTask;

        var completeOnAnyOtherTask = await Task.WhenAny(writeTasks).ConfigureAwait(false);

        _ = BackgroundTask.Run(
            async () => {
                await Task.WhenAll(writeTasks).ConfigureAwait(false);
                await completeOnSelfTask.ConfigureAwait(false);
            },
            e => Log.LogError(e, "Sending audio stream #{StreamId} to replicas has failed", streamId),
            cancellationToken);

        return Task.WhenAny(completeOnSelfTask, completeOnAnyOtherTask);
    }
}
