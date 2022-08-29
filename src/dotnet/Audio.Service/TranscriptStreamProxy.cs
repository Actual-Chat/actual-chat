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

        var result = await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
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
            var client = await GetTranscriptStreamClient(address).ConfigureAwait(false);
            result = await client.Read(streamId, cancellationToken).ConfigureAwait(false);
            if (result.HasValue) {
                DebugLog?.LogInformation("Read(#{StreamId}): found the stream on {Address}", streamId, address);
                return result;
            }
        }
        DebugLog?.LogInformation("Read(#{StreamId}): no stream found", streamId);
        return result;

        async Task<ITranscriptStreamServer> GetTranscriptStreamClient(string address)
            => OrdinalEquals(address, kube.PodIP)
                ? TranscriptStreamServer
                : await AudioHubBackendClientFactory.GetTranscriptStreamClient(address, port, cancellationToken).ConfigureAwait(false);
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
        var addresses = addressRing.GetMany(streamId.Value.GetDjb2HashCode(), WriteReplicaCount).ToList();
        DebugLog?.LogInformation("Write(#{StreamId}): hitting {Addresses}", streamId, addresses.ToDelimitedString());
        var writeTasks = await addresses
            .Select(async address => {
                var client = await GetTranscriptStreamClient(address).ConfigureAwait(false);
                return await client.StartWrite(streamId, memoized.Replay(cancellationToken), cancellationToken).ConfigureAwait(false);
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

        async Task<ITranscriptStreamServer> GetTranscriptStreamClient(string address)
            => OrdinalEquals(address, kube.PodIP)
                ? TranscriptStreamServer
                : await AudioHubBackendClientFactory.GetTranscriptStreamClient(address, port, cancellationToken).ConfigureAwait(false);
    }
}
