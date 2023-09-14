using System.Net.WebSockets;
using ActualChat.Kubernetes;
using ActualChat.Transcription;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Audio;

public class TranscriptStreamProxy : ITranscriptStreamServer
{
    private const int WriteReplicaCount = 2;
    private const int ReadReplicaCount = 2;
    private const int WriteAttemptCount = 2;
    private const int ReadAttemptCount = 2;
    private TimeSpan ReadStreamWaitTimeout { get; } = TimeSpan.FromSeconds(1);

    private AudioSettings Settings { get; }
    private TranscriptStreamServer TranscriptStreamServer { get; }
    private AudioHubBackendClientFactory AudioHubBackendClientFactory { get; }
    private KubeServices KubeServices { get; }
    private MomentClockSet Clocks { get; }
    private ILogger<TranscriptStreamProxy> Log { get; }
    private ILogger? DebugLog { get; }

    public TranscriptStreamProxy(
        AudioSettings settings,
        TranscriptStreamServer transcriptStreamServer,
        AudioHubBackendClientFactory audioHubBackendClientFactory,
        KubeServices kubeServices,
        MomentClockSet clocks,
        ILogger<TranscriptStreamProxy> log)
    {
        Settings = settings;
        TranscriptStreamServer = transcriptStreamServer;
        AudioHubBackendClientFactory = audioHubBackendClientFactory;
        KubeServices = kubeServices;
        Clocks = clocks;
        Log = log;
        DebugLog = Constants.DebugMode.TranscriptStreamProxy ? Log : null;
    }

    public async Task<IAsyncEnumerable<TranscriptDiff>> Read(Symbol streamId, CancellationToken cancellationToken)
    {
        var streamName = $"Transcript #{streamId.Value}";
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null) {
            DebugLog?.LogInformation("Read({Stream}): fallback to the local server", streamName);
            return await TranscriptStreamServer.Read(streamId, cancellationToken).ConfigureAwait(false);
        }

        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.
        var target = Channel.CreateBounded<TranscriptDiff>(
            new BoundedChannelOptions(Constants.Queues.OpusStreamConverterQueueSize) {
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        _ = BackgroundTask.Run(async () => {
            var retryCount = 0;
            var kubeService = new KubeService(Settings.Namespace, Settings.ServiceName);
            var endpointState = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
            try {
                while (!cancellationToken.IsCancellationRequested && retryCount++ < ReadAttemptCount)
                    try {
                        var stream = await ReadFromReplica(endpointState.Value, cancellationToken)
                            .ConfigureAwait(false);
                        await foreach (var chunk in stream.ConfigureAwait(false)) {
                            await target.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                        }
                        return;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                        Log.LogWarning("Retry reading transcript stream {StreamId}", streamId);
                    }
                    catch (WebSocketException) when (!cancellationToken.IsCancellationRequested) {
                        Log.LogWarning("Retry reading transcript stream {StreamId} on network error", streamId);
                    }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Error reading transcript stream");
                target.Writer.TryComplete(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
            }
        }, cancellationToken);

        return target.Reader.ReadAllAsync(cancellationToken);

        async Task<IAsyncEnumerable<TranscriptDiff>> ReadFromReplica(KubeServiceEndpoints serviceEndpoints, CancellationToken cancellationToken1)
        {
            var addressRing = serviceEndpoints.GetAddressHashRing();
            if (addressRing.IsEmpty) {
                Log.LogError("Read({Stream}): empty address ring!", streamName);
                if (TranscriptStreamServer.IsStreamExists(streamId))
                    return await TranscriptStreamServer.Read(streamId, cancellationToken1);
                return AsyncEnumerable.Empty<TranscriptDiff>();
            }
            var port = serviceEndpoints.GetPort()!.Port;
            var addresses = addressRing.Segment(streamId.Value.GetDjb2HashCode(), WriteReplicaCount);
            var readReplicaCount = addresses.Count.Clamp(0, ReadReplicaCount);

            DebugLog?.LogInformation("Read({Stream}): hitting [{Addresses}]", streamName, addresses.ToDelimitedString());
            var randomizedAddresses = addresses.Shuffle().Take(readReplicaCount);
            foreach (var address in randomizedAddresses) {
                DebugLog?.LogInformation("Read({Stream}): trying {Address}", streamName, address);
                using var client = await GetTranscriptStreamClient(kube, address, port, cancellationToken1).ConfigureAwait(false);
                var stream = await client.Read(streamId,  cancellationToken1).ConfigureAwait(false);
                var result = await stream.IsNonEmpty(Clocks.CpuClock, ReadStreamWaitTimeout, cancellationToken1)
                    .ConfigureAwait(false);
                if (result.IsSome(out var s)) {
                    DebugLog?.LogInformation("Read({Stream}): found the stream on {Address}", streamName, address);
                    return s;
                }
            }
            DebugLog?.LogInformation("Read({Stream}): no stream found", streamName);
            if (TranscriptStreamServer.IsStreamExists(streamId))
                return await TranscriptStreamServer.Read(streamId, cancellationToken1);
            return AsyncEnumerable.Empty<TranscriptDiff>();
        }
    }

    public async Task Write(Symbol streamId, IAsyncEnumerable<TranscriptDiff> stream, CancellationToken cancellationToken)
    {
        var streamName = $"Transcript #{streamId.Value}";
        var kube = await KubeServices.GetKube(cancellationToken).ConfigureAwait(false);
        if (kube == null) {
            DebugLog?.LogInformation("Write({Stream}): fallback to the local server", streamName);
            await TranscriptStreamServer.Write(streamId, stream, cancellationToken).ConfigureAwait(false);
            return;
        }

        var kubeService = new KubeService(Settings.Namespace, Settings.ServiceName);
        using var endpointState = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
        var serviceEndpoints = endpointState.Value;
        var addressRing = serviceEndpoints.GetAddressHashRing();
        if (addressRing.IsEmpty) {
            Log.LogError("Write({Stream}): empty address ring, writing locally!", streamName);
            await TranscriptStreamServer.Write(streamId, stream, cancellationToken).ConfigureAwait(false);
            return;
        }
        var port = serviceEndpoints.GetPort()!.Port;
        var memoized = stream.Memoize(cancellationToken);
        var isNotCompletedYet = true;
        var replicasLeft = WriteReplicaCount;
        var writeTasks = new HashSet<Task>();
        var addresses = endpointState.Value
            .GetAddressHashRing()
            .Segment(streamId.Value.GetDjb2HashCode(), replicasLeft);
        DebugLog?.LogInformation("Write({Stream}): hitting [{Addresses}]", streamName, addresses.ToDelimitedString());
        writeTasks.AddRange(addresses.Select(a => WriteToReplica(a, cancellationToken)));
        var retryCount = 0;
        while (!cancellationToken.IsCancellationRequested && isNotCompletedYet && retryCount <= WriteAttemptCount) {
            await Task.WhenAny(writeTasks).ConfigureAwait(false);

            var tasksToRetry = writeTasks
                .Where(t => t.IsFaulted && (t.Exception?.InnerExceptions.Any(e => e is WebSocketException) ?? false))
                .ToList();
            writeTasks.RemoveWhere(t => t.IsCompletedSuccessfully);
            if (tasksToRetry.Count > 0) {
                // Get fresh state value
                addresses = endpointState.Value
                    .GetAddressHashRing()
                    .Segment(streamId.Value.GetDjb2HashCode(), tasksToRetry.Count);
                Log.LogWarning("Write({Stream}): retrying write to [{Addresses}]", streamName, addresses.ToDelimitedString());
                writeTasks.AddRange(addresses.Select(a => WriteToReplica(a, cancellationToken)));
                retryCount++;
            }
            else
                isNotCompletedYet = writeTasks.Any(t => !t.IsCompleted);
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (writeTasks.Count > 0)
            await Task.WhenAll(writeTasks).ConfigureAwait(false);
        return;

        async Task WriteToReplica(string address, CancellationToken cancellationToken1)
        {
            DebugLog?.LogInformation("Write({Stream}): writing to {Address} started", streamName, address);
            var client = await GetTranscriptStreamClient(kube, address, port, cancellationToken1).ConfigureAwait(false);
            try {
                await client.Write(streamId, memoized.Replay(cancellationToken1), cancellationToken1).ConfigureAwait(false);
                DebugLog?.LogInformation("Write({Stream}): done writing to {Address}", streamName, address);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, "Write({Stream}): failed writing to {Address}", streamName, address);
            }
        }
    }

    public void Dispose()
    { }

    private async Task<ITranscriptStreamServer> GetTranscriptStreamClient(
        Kube kube, string address, int port, CancellationToken cancellationToken)
        => OrdinalEquals(address, kube.PodIP) && !kube.IsEmulated
            ? TranscriptStreamServer.SkipDispose()
            : await AudioHubBackendClientFactory.GetTranscriptStreamClient(address, port, cancellationToken).ConfigureAwait(false);
}
