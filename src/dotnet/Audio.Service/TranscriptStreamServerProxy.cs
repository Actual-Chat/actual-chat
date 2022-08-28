using ActualChat.Kubernetes;
using ActualChat.Transcription;

namespace ActualChat.Audio;

public class TranscriptStreamServerProxy : ITranscriptStreamServer
{
    private TranscriptStreamServer TranscriptStreamServer { get; }
    private AudioHubBackendClientFactory AudioHubBackendClientFactory { get; }
    private ServiceRegistry ServiceRegistry { get; }
    private AudioSettings Settings { get; }
    private ILogger<TranscriptStreamServerProxy> Log { get; }

    public TranscriptStreamServerProxy(
        TranscriptStreamServer transcriptStreamServer,
        AudioHubBackendClientFactory audioHubBackendClientFactory,
        ServiceRegistry serviceRegistry,
        AudioSettings settings,
        ILogger<TranscriptStreamServerProxy> log)
    {
        TranscriptStreamServer = transcriptStreamServer;
        AudioHubBackendClientFactory = audioHubBackendClientFactory;
        ServiceRegistry = serviceRegistry;
        Settings = settings;
        Log = log;
    }

    public async Task<Option<IAsyncEnumerable<Transcript>>> Read(Symbol streamId, CancellationToken cancellationToken)
    {
        if (KubernetesInfo.POD_IP.IsNullOrEmpty()
            || await KubernetesConfig.IsInCluster(cancellationToken).ConfigureAwait(false))
            return await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);

        var transcriptStreamOption =
            await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        if (transcriptStreamOption.HasValue)
            return transcriptStreamOption;

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
            .Where(a => !string.Equals(a, KubernetesInfo.POD_IP, StringComparison.Ordinal))
            .OrderBy(a => a.GetHashCode() * (long)streamId.HashCode)
            .ToList();
        if (alternateAddresses.Count == 0 || !port.HasValue)
            return transcriptStreamOption;

        foreach (var alternateAddress in alternateAddresses) {
            var client = await AudioHubBackendClientFactory.GetTranscriptStreamClient(alternateAddress, port.Value, cancellationToken).ConfigureAwait(false);
            var streamOption = await client.Read(streamId, cancellationToken).ConfigureAwait(false);
            if (streamOption.HasValue)
                return streamOption;
        }
        return transcriptStreamOption;
    }

    public async Task<Task> Write(
        Symbol streamId,
        IAsyncEnumerable<Transcript> transcriptStream,
        CancellationToken cancellationToken)
    {
        if (KubernetesInfo.POD_IP.IsNullOrEmpty()
            || await KubernetesConfig.IsInCluster(cancellationToken).ConfigureAwait(false))
            return await TranscriptStreamServer.Write(streamId, transcriptStream, cancellationToken).ConfigureAwait(false);

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
            return await TranscriptStreamServer.Write(streamId, transcriptStream, cancellationToken).ConfigureAwait(false);

        var memoized = transcriptStream.Memoize(cancellationToken);
        var completeOnSelfTask = TranscriptStreamServer.Write(streamId, memoized);
        var writeTasks = await alternateAddresses
            .Select(alternateAddress => AudioHubBackendClientFactory.GetTranscriptStreamClient(alternateAddress, port.Value, cancellationToken))
            .Select(async clientTask => {
                var client = await clientTask.ConfigureAwait(false);
                return client.Write(streamId, memoized.Replay(cancellationToken), cancellationToken);
            })
            .Collect()
            .ConfigureAwait(false);

        if (writeTasks.Count == 0)
            return completeOnSelfTask;

        var completeOnAnyOtherTask = await Task.WhenAny(writeTasks).ConfigureAwait(false);

        _ = Task.Run(
            async () => {
                try {
                    await Task.WhenAll(writeTasks).ConfigureAwait(false);
                    await completeOnSelfTask.ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException) {
                    Log.LogError(e, "Sending transcript stream {StreamId} to replicas has failed", streamId);
                    throw;
                }
            },
            cancellationToken);

        return Task.WhenAny(completeOnSelfTask, completeOnAnyOtherTask);
    }
}
