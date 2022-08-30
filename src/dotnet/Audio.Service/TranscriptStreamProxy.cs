using ActualChat.Kubernetes;
using ActualChat.Transcription;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Audio;

public class TranscriptStreamProxy : ITranscriptStreamServer
{
    private const int WriteReplicaCount = 2;
    private const int ReadReplicaCount = 1;

    private TranscriptStreamServer TranscriptStreamServer { get; }
    private AudioHubBackendClientFactory AudioHubBackendClientFactory { get; }
    private KubeServices KubeServices { get; }
    private AudioSettings Settings { get; }
    private ILogger<TranscriptStreamProxy> Log { get; }
    private ILogger? DebugLog { get; }

    public TranscriptStreamProxy(
        TranscriptStreamServer transcriptStreamServer,
        AudioHubBackendClientFactory audioHubBackendClientFactory,
        KubeServices kubeServices,
        AudioSettings settings,
        ILogger<TranscriptStreamProxy> log)
    {
        TranscriptStreamServer = transcriptStreamServer;
        AudioHubBackendClientFactory = audioHubBackendClientFactory;
        KubeServices = kubeServices;
        Settings = settings;
        Log = log;
        DebugLog = Constants.DebugMode.TranscriptStreamProxy ? Log : null;
    }

    public async Task<Option<IAsyncEnumerable<Transcript>>> Read(Symbol streamId, CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null) {
            DebugLog?.LogInformation("Read(#{StreamId}): fallback to the local server", streamId);
            return await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        }

        var result = Option.None<IAsyncEnumerable<Transcript>>();
        if (!kube.IsEmulated) {
            result = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
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

            var client = await GetTranscriptStreamClient(kube, address, port, cancellationToken).ConfigureAwait(false);
            result = await client.Read(streamId, cancellationToken).ConfigureAwait(false);
            if (result.HasValue) {
                DebugLog?.LogInformation("Read(#{StreamId}): found the stream on {Address}", streamId, address);
                return result;
            }
        }
        DebugLog?.LogInformation("Read(#{StreamId}): no stream found", streamId);
        return result;
    }

    public async Task<Task> StartWrite(
        Symbol streamId,
        IAsyncEnumerable<Transcript> transcriptStream,
        CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null) {
            DebugLog?.LogInformation("Write(#{StreamId}): fallback to the local server", streamId);
            return await TranscriptStreamServer.StartWrite(streamId, transcriptStream, cancellationToken).ConfigureAwait(false);
        }

        var kubeService = new KubeService(Settings.Namespace, Settings.ServiceName);
        using var endpointState = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.LatestNonErrorValue;

        var addressRing = serviceEndpoints.GetAddressHashRing();
        if (addressRing.IsEmpty) {
            Log.LogError("Write(#{StreamId}): empty address ring, writing locally!", streamId);
            return await TranscriptStreamServer.StartWrite(streamId, transcriptStream, cancellationToken).ConfigureAwait(false);
        }
        var port = serviceEndpoints.GetPort()!.Port;

        var memoized = transcriptStream.Memoize(cancellationToken);
        var addresses = addressRing.Segment(streamId.Value.GetDjb2HashCode(), WriteReplicaCount);
        DebugLog?.LogInformation("Write(#{StreamId}): hitting [{Addresses}]", streamId, addresses.ToDelimitedString());
        var writeTasks = addresses
            .Select(async address => {
                var client = await GetTranscriptStreamClient(kube, address, port, cancellationToken).ConfigureAwait(false);
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

    private async Task<ITranscriptStreamServer> GetTranscriptStreamClient(
        Kube kube, string address, int port, CancellationToken cancellationToken)
        => OrdinalEquals(address, kube.PodIP) && !kube.IsEmulated
            ? TranscriptStreamServer
            : await AudioHubBackendClientFactory.GetTranscriptStreamClient(address, port, cancellationToken).ConfigureAwait(false);
}
