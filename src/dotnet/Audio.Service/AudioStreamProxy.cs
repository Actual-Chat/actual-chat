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

        var result = Option.None<IAsyncEnumerable<byte[]>>();
        if (!kube.IsEmulated) {
            result = await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
            if (result.HasValue) {
                DebugLog?.LogInformation("Read(#{StreamId}): found the stream on the local server", streamId);
                return result;
            }
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

        var addresses = addressRing.Segment(streamId.Value.GetDjb2HashCode(), WriteReplicaCount);
        var readReplicaCount = addresses.Count.Clamp(0, ReadReplicaCount);
        DebugLog?.LogInformation("Read(#{StreamId}): hitting [{Addresses}]", streamId, addresses.ToDelimitedString());
        for (var i = 0; i < readReplicaCount; i++) {
            var address = addresses.GetRandom();
            DebugLog?.LogInformation("Read(#{StreamId}): trying {Address}", streamId, address);

            var client = await GetAudioStreamClient(kube, address, port, cancellationToken).ConfigureAwait(false);
            result = await client.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
            if (result.HasValue) {
                DebugLog?.LogInformation("Read(#{StreamId}): found the stream on {Address}", streamId, address);
                return result;
            }
        }
        DebugLog?.LogInformation("Read(#{StreamId}): no stream found", streamId);
        return result;
    }

    public async Task<Task> StartWrite(Symbol streamId, IAsyncEnumerable<byte[]> audioStream, CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null) {
            DebugLog?.LogInformation("Write(#{StreamId}): fallback to the local server", streamId);
            return await AudioStreamServer.StartWrite(streamId, audioStream, cancellationToken).ConfigureAwait(false);
        }

        var kubeService = new KubeService(Settings.Namespace, Settings.ServiceName);
        using var endpointState = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.LatestNonErrorValue;
        var addressRing = serviceEndpoints.GetAddressHashRing();
        if (addressRing.IsEmpty) {
            Log.LogError("Write(#{StreamId}): empty address ring, writing locally!", streamId);
            return await AudioStreamServer.StartWrite(streamId, audioStream, cancellationToken).ConfigureAwait(false);
        }
        var port = serviceEndpoints.GetPort()!.Port;

        var memoized = audioStream.Memoize(cancellationToken);
        var addresses = addressRing.Segment(streamId.Value.GetDjb2HashCode(), WriteReplicaCount);
        DebugLog?.LogInformation("Write(#{StreamId}): hitting [{Addresses}]", streamId, addresses.ToDelimitedString());
        var writeTasks = addresses
            .Select(async address => {
                var client = await GetAudioStreamClient(kube, address, port, cancellationToken).ConfigureAwait(false);
                var doneTask = await client.StartWrite(streamId, memoized.Replay(cancellationToken), cancellationToken).ConfigureAwait(false);

                if (DebugLog != null) {
                    DebugLog.LogInformation("Write(#{StreamId}): writing to {Address} started", streamId, address);
                    _ = doneTask.ContinueWith(
                        _ => DebugLog.LogInformation("Write(#{StreamId}): done writing to {Address}", streamId, address),
                        TaskScheduler.Default);
                }
                return doneTask;
            })
            .ToList();

        var doneTask = BackgroundTask.Run(
            async () => {
                var doneTasks = await writeTasks.Collect().ConfigureAwait(false);
                await Task.WhenAll(doneTasks).ConfigureAwait(false);
            },
            e => Log.LogError(e, "Write(#{StreamId}): write to one of replicas failed", streamId),
            cancellationToken);

        await Task.WhenAll(writeTasks).ConfigureAwait(false);
        return doneTask;
    }

    private async Task<IAudioStreamServer> GetAudioStreamClient(
        Kube kube, string address, int port, CancellationToken cancellationToken)
        => OrdinalEquals(address, kube.PodIP) && !kube.IsEmulated
            ? AudioStreamServer
            : await AudioHubBackendClientFactory.GetAudioStreamClient(address, port, cancellationToken).ConfigureAwait(false);
}
