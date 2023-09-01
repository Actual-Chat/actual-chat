using ActualChat.Kubernetes;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Audio;

public class AudioStreamProxy : IAudioStreamServer
{
    private const int WriteReplicaCount = 2;
    private const int ReadReplicaCount = 2;
    private TimeSpan ReadStreamWaitTimeout { get; } = TimeSpan.FromSeconds(1);

    private AudioSettings Settings { get; }
    private AudioStreamServer AudioStreamServer { get; }
    private AudioHubBackendClientFactory AudioHubBackendClientFactory { get; }
    private KubeServices KubeServices { get; }
    private MomentClockSet Clocks { get; }
    private ILogger<AudioStreamProxy> Log { get; }
    private ILogger? DebugLog { get; }

    public AudioStreamProxy(
        AudioSettings settings,
        AudioStreamServer audioStreamServer,
        AudioHubBackendClientFactory audioHubBackendClientFactory,
        KubeServices kubeServices,
        MomentClockSet clocks,
        ILogger<AudioStreamProxy> log)
    {
        Settings = settings;
        AudioStreamServer = audioStreamServer;
        AudioHubBackendClientFactory = audioHubBackendClientFactory;
        KubeServices = kubeServices;
        Clocks = clocks;
        Log = log;
        DebugLog = Constants.DebugMode.AudioStreamProxy ? Log : null;
    }

    public async Task<IAsyncEnumerable<byte[]>> Read(Symbol streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var streamName = $"audio #{streamId.Value}";
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null) {
            DebugLog?.LogInformation("Read({Stream}): fallback to the local server", streamName);
            return await AudioStreamServer.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
        }

        var kubeService = new KubeService(Settings.Namespace, Settings.ServiceName);
        using var endpointState = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.Value;
        var addressRing = serviceEndpoints.GetAddressHashRing();
        if (addressRing.IsEmpty) {
            Log.LogError("Read({Stream}): empty address ring!", streamName);
            return AsyncEnumerable.Empty<byte[]>();
        }
        var port = serviceEndpoints.GetPort()!.Port;
        var addresses = addressRing.Segment(streamId.Value.GetDjb2HashCode(), WriteReplicaCount);
        var readReplicaCount = addresses.Count.Clamp(0, ReadReplicaCount);

        DebugLog?.LogInformation("Read({Stream}): hitting [{Addresses}]", streamName, addresses.ToDelimitedString());
        var randomizedAddresses = addresses.Shuffle().Take(readReplicaCount);
        foreach (var address in randomizedAddresses) {
            DebugLog?.LogInformation("Read({Stream}): trying {Address}", streamName, address);
            var client = await GetAudioStreamClient(kube, address, port, cancellationToken).ConfigureAwait(false);
            var stream = await client.Read(streamId, skipTo, cancellationToken).ConfigureAwait(false);
            var result = await stream.IsNonEmpty(Clocks.CpuClock, ReadStreamWaitTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSome(out var s)) {
                DebugLog?.LogInformation("Read({Stream}): found the stream on {Address}", streamName, address);
                return s;
            }
        }
        DebugLog?.LogInformation("Read({Stream}): no stream found", streamName);
        return AsyncEnumerable.Empty<byte[]>();
    }

    public async Task Write(Symbol streamId, IAsyncEnumerable<byte[]> stream, CancellationToken cancellationToken)
    {
        var streamName = $"audio #{streamId.Value}";
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null) {
            DebugLog?.LogInformation("Write({Stream}): fallback to the local server", streamName);
            await AudioStreamServer.Write(streamId, stream, cancellationToken).ConfigureAwait(false);
            return;
        }

        var kubeService = new KubeService(Settings.Namespace, Settings.ServiceName);
        using var endpointState = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.Value;
        var addressRing = serviceEndpoints.GetAddressHashRing();
        if (addressRing.IsEmpty) {
            Log.LogError("Write({Stream}): empty address ring, writing locally!", streamName);
            await AudioStreamServer.Write(streamId, stream, cancellationToken).ConfigureAwait(false);
            return;
        }
        var port = serviceEndpoints.GetPort()!.Port;
        var addresses = addressRing.Segment(streamId.Value.GetDjb2HashCode(), WriteReplicaCount);

        DebugLog?.LogInformation("Write({Stream}): hitting [{Addresses}]", streamName, addresses.ToDelimitedString());
        var memoized = stream.Memoize(cancellationToken);
        var writeTasks = addresses
            .Select(async address => {
                DebugLog?.LogInformation("Write({Stream}): writing to {Address} started", streamName, address);
                var client = await GetAudioStreamClient(kube, address, port, cancellationToken).ConfigureAwait(false);
                try {
                    await client.Write(streamId, memoized.Replay(cancellationToken), cancellationToken).ConfigureAwait(false);
                    DebugLog?.LogInformation("Write({Stream}): done writing to {Address}", streamName, address);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    Log.LogError(e, "Write({Stream}): failed writing to {Address}", streamName, address);
                }
            })
            .ToList();
        await Task.WhenAll(writeTasks).ConfigureAwait(false);
    }

    private async Task<IAudioStreamServer> GetAudioStreamClient(
        Kube kube, string address, int port, CancellationToken cancellationToken)
        => OrdinalEquals(address, kube.PodIP) && !kube.IsEmulated
            ? AudioStreamServer
            : await AudioHubBackendClientFactory.GetAudioStreamClient(address, port, cancellationToken).ConfigureAwait(false);
}
