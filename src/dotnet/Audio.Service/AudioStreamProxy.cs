using ActualChat.Kubernetes;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Audio;

public class AudioStreamProxy : IAudioStreamServer
{
    private const int WriteReplicaCount = 2;
    private const int ReadReplicaCount = 1;

    private AudioSettings Settings { get; }
    private AudioStreamServer AudioStreamServer { get; }
    private AudioHubBackendClientFactory AudioHubBackendClientFactory { get; }
    private KubeServices KubeServices { get; }
    private ILogger<AudioStreamProxy> Log { get; }
    private ILogger? DebugLog { get; }

    public AudioStreamProxy(
        AudioStreamServer audioStreamServer,
        AudioHubBackendClientFactory audioHubBackendClientFactory,
        KubeServices kubeServices,
        AudioSettings settings,
        ILogger<AudioStreamProxy> log)
    {
        Settings = settings;
        AudioStreamServer = audioStreamServer;
        AudioHubBackendClientFactory = audioHubBackendClientFactory;
        KubeServices = kubeServices;
        Log = log;
        DebugLog = Constants.DebugMode.AudioStreamProxy ? Log : null;
    }

    public async Task<Option<IAsyncEnumerable<byte[]>>> Read(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null) {
            DebugLog?.LogInformation("Read(#{StreamId}): fallback to the local server", streamId);
            return await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        }

        var result = await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        if (result.HasValue) {
            DebugLog?.LogInformation("Read(#{StreamId}): found the stream on the local server", streamId);
            return result;
        }

        var kubeService = new KubeService(Settings.Namespace, Settings.ServiceName);
        using var endpointState = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.LatestNonErrorValue;

        var addressRing = serviceEndpoints.GetAddressHashRing();
        if (addressRing.IsEmpty) {
            Log.LogError("Read(#{StreamId}): empty address ring!", streamId);
            return result;
        }
        var port = serviceEndpoints.GetPort()!.Port;

        var addresses = addressRing
            .GetManyRandom(streamId.Value.GetDjb2HashCode(), ReadReplicaCount, WriteReplicaCount)
            .ToList();
        DebugLog?.LogInformation("Read(#{StreamId}): hitting {Addresses}", streamId, addresses.ToDelimitedString());
        foreach (var address in addresses) {
            var client = await GetAudioStreamClient(address).ConfigureAwait(false);
            result = await client.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
            if (result.HasValue) {
                DebugLog?.LogInformation("Read(#{StreamId}): found the stream on {Address}", streamId, address);
                return result;
            }
        }
        DebugLog?.LogInformation("Read(#{StreamId}): no stream found", streamId);
        return result;

        async Task<IAudioStreamServer> GetAudioStreamClient(string address)
            => OrdinalEquals(address, kube.PodIP)
                ? AudioStreamServer
                : await AudioHubBackendClientFactory.GetAudioStreamClient(address, port, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Task> Write(Symbol streamId, IAsyncEnumerable<byte[]> audioStream, CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null) {
            DebugLog?.LogInformation("Write(#{StreamId}): fallback to the local server", streamId);
            return await AudioStreamServer.Write(streamId, audioStream, cancellationToken).ConfigureAwait(false);
        }

        var kubeService = new KubeService(Settings.Namespace, Settings.ServiceName);
        using var endpointState = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.LatestNonErrorValue;

        var addressRing = serviceEndpoints.GetAddressHashRing();
        if (addressRing.IsEmpty) {
            Log.LogError("Write(#{StreamId}): empty address ring, writing locally!", streamId);
            return await AudioStreamServer.Write(streamId, audioStream, cancellationToken).ConfigureAwait(false);
        }
        var port = serviceEndpoints.GetPort()!.Port;

        var memoized = audioStream.Memoize(cancellationToken);
        var addresses = addressRing.GetMany(streamId.Value.GetDjb2HashCode(), WriteReplicaCount).ToList();
        DebugLog?.LogInformation("Write(#{StreamId}): hitting {Addresses}", streamId, addresses.ToDelimitedString());
        var writeTasks = await addresses
            .Select(async address => {
                var client = await GetAudioStreamClient(address).ConfigureAwait(false);
                return await client.Write(streamId, memoized.Replay(cancellationToken), cancellationToken).ConfigureAwait(false);
            })
            .Collect()
            .ConfigureAwait(false);

        _ = BackgroundTask.Run(
            () => Task.WhenAll(writeTasks),
            e => Log.LogError(e, "Write(#{StreamId}): write to one of replicas failed", streamId),
            cancellationToken);

        // Let's wait for completion of at least one write
        var completedTask = await Task.WhenAny(writeTasks).ConfigureAwait(false);
        return completedTask;

        async Task<IAudioStreamServer> GetAudioStreamClient(string address)
            => OrdinalEquals(address, kube.PodIP)
                ? AudioStreamServer
                : await AudioHubBackendClientFactory.GetAudioStreamClient(address, port, cancellationToken).ConfigureAwait(false);
    }
}
