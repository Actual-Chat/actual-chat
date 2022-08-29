using ActualChat.Kubernetes;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Audio;

public class AudioStreamServerProxy : IAudioStreamServer
{
    private AudioSettings Settings { get; }
    private AudioStreamServer AudioStreamServer { get; }
    private AudioHubBackendClientFactory AudioHubBackendClientFactory { get; }
    private KubeServices KubeServices { get; }
    private ILogger<AudioStreamServerProxy> Log { get; }

    public AudioStreamServerProxy(
        AudioStreamServer audioStreamServer,
        AudioHubBackendClientFactory audioHubBackendClientFactory,
        KubeServices kubeServices,
        AudioSettings settings,
        ILogger<AudioStreamServerProxy> log)
    {
        Settings = settings;
        AudioStreamServer = audioStreamServer;
        AudioHubBackendClientFactory = audioHubBackendClientFactory;
        KubeServices = kubeServices;
        Log = log;
    }

    public async Task<Option<IAsyncEnumerable<byte[]>>> Read(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null)
            return await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);

        var result = await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        if (result.HasValue)
            return result;

        // TODO(AK): use consistent hashing to get service replicas
        var endpointState = await KubeServices.GetServiceEndpoints(Settings.Namespace, Settings.ServiceName, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.LatestNonErrorValue;
        var port = serviceEndpoints.Ports
            .Where(p => OrdinalEquals(p.Name, "http"))
            .Select(p => (int?)p.Port)
            .FirstOrDefault();
        var alternateAddresses = serviceEndpoints.Endpoints
            .Where(e => e.IsReady)
            .SelectMany(e => e.Addresses)
            .Where(a => !OrdinalEquals(a, kube.PodIP))
            .OrderBy(a => a.GetDjb2HashCode() * (long)streamId.HashCode % 33461)
            .ToList();
        if (alternateAddresses.Count == 0 || !port.HasValue)
            return result;

        foreach (var alternateAddress in alternateAddresses) {
            var client = await AudioHubBackendClientFactory.GetAudioStreamClient(alternateAddress, port.Value, cancellationToken).ConfigureAwait(false);
            result = await client.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
            if (result.HasValue)
                return result;
        }
        return result;
    }

    public async Task<Task> Write(Symbol streamId, IAsyncEnumerable<byte[]> audioStream, CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null)
            return await AudioStreamServer.Write(streamId, audioStream, cancellationToken).ConfigureAwait(false);

        // TODO(AK): use consistent hashing to get service replicas
        var endpointState = await KubeServices
            .GetServiceEndpoints(Settings.Namespace, Settings.ServiceName, cancellationToken)
            .ConfigureAwait(false);
        var serviceEndpoints = endpointState.LatestNonErrorValue;
        var port = serviceEndpoints.Ports
            .Where(p => OrdinalEquals(p.Name, "http"))
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
