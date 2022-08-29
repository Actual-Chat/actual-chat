using ActualChat.Kubernetes;
using ActualChat.Transcription;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Audio;

public class TranscriptStreamServerProxy : ITranscriptStreamServer
{
    private TranscriptStreamServer TranscriptStreamServer { get; }
    private AudioHubBackendClientFactory AudioHubBackendClientFactory { get; }
    private KubeServices KubeServices { get; }
    private AudioSettings Settings { get; }
    private ILogger<TranscriptStreamServerProxy> Log { get; }

    public TranscriptStreamServerProxy(
        TranscriptStreamServer transcriptStreamServer,
        AudioHubBackendClientFactory audioHubBackendClientFactory,
        KubeServices kubeServices,
        AudioSettings settings,
        ILogger<TranscriptStreamServerProxy> log)
    {
        TranscriptStreamServer = transcriptStreamServer;
        AudioHubBackendClientFactory = audioHubBackendClientFactory;
        KubeServices = kubeServices;
        Settings = settings;
        Log = log;
    }

    public async Task<Option<IAsyncEnumerable<Transcript>>> Read(Symbol streamId, CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null)
            return await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);

        var resultOpt =
            await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        if (resultOpt.HasValue)
            return resultOpt;

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
            .Where(a => !OrdinalEquals(a, kube.PodIP))
            .OrderBy(a => a.GetDjb2HashCode() * (long)streamId.HashCode % 33461)
            .ToList();
        if (alternateAddresses.Count == 0 || !port.HasValue)
            return resultOpt;

        foreach (var alternateAddress in alternateAddresses) {
            var client = await AudioHubBackendClientFactory.GetTranscriptStreamClient(alternateAddress, port.Value, cancellationToken).ConfigureAwait(false);
            resultOpt = await client.Read(streamId, cancellationToken).ConfigureAwait(false);
            if (resultOpt.HasValue)
                return resultOpt;
        }
        return resultOpt;
    }

    public async Task<Task> Write(
        Symbol streamId,
        IAsyncEnumerable<Transcript> transcriptStream,
        CancellationToken cancellationToken)
    {
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null)
            return await TranscriptStreamServer.Write(streamId, transcriptStream, cancellationToken).ConfigureAwait(false);

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

        _ = BackgroundTask.Run(
            async () => {
                await Task.WhenAll(writeTasks).ConfigureAwait(false);
                await completeOnSelfTask.ConfigureAwait(false);
            },
            e => Log.LogError(e, "Sending transcript stream #{StreamId} to replicas has failed", streamId),
            cancellationToken);

        return Task.WhenAny(completeOnSelfTask, completeOnAnyOtherTask);
    }
}
