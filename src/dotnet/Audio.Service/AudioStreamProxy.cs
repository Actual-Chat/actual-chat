using System.Net.WebSockets;
using ActualChat.Kubernetes;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Audio;

public class AudioStreamProxy : IAudioStreamServer
{
    private const int WriteReplicaCount = 2;
    private const int ReadReplicaCount = 2;
    private const int WriteAttemptCount = 2;
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

        // We're doing this fairly complex processing via tasks & channels only
        // because "async IAsyncEnumerable<..>" methods can't contain
        // "yield return" inside "catch" blocks, and we need this here.
        var target = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(Constants.Queues.OpusStreamConverterQueueSize) {
                SingleWriter = true,
                SingleReader = true,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        _ = BackgroundTask.Run(async () => {
            var counter = 0;
            var currentSkipTo = skipTo;
            var kubeService = new KubeService(Settings.Namespace, Settings.ServiceName);
            var endpointState = await KubeServices.GetServiceEndpoints(kubeService, cancellationToken).ConfigureAwait(false);
            try {
                while (!cancellationToken.IsCancellationRequested)
                    try {
                        var stream = await ReadFromReplica(endpointState.Value, currentSkipTo, cancellationToken)
                            .ConfigureAwait(false);
                        await foreach (var chunk in stream.ConfigureAwait(false)) {
                            counter++;
                            await target.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                        }
                        return;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                        currentSkipTo = skipTo.Add(TimeSpan.FromMilliseconds(20 * counter));
                        Log.LogWarning("Retry reading audio stream {StreamId} with offset {SkipTo}", streamId, currentSkipTo);
                    }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException e) {
                target.Writer.TryComplete(e);
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Error reading audio stream");
                target.Writer.TryComplete(e);
                throw;
            }
            finally {
                target.Writer.TryComplete();
            }
        }, cancellationToken);

        return target.Reader.ReadAllAsync(cancellationToken);

        async Task<IAsyncEnumerable<byte[]>> ReadFromReplica(KubeServiceEndpoints serviceEndpoints, TimeSpan skipTo1, CancellationToken cancellationToken1)
        {
            var addressRing = serviceEndpoints.GetAddressHashRing();
            if (addressRing.IsEmpty) {
                Log.LogError("Read({Stream}): empty address ring!", streamName);
                if (AudioStreamServer.IsStreamExists(streamId))
                    return await AudioStreamServer.Read(streamId, skipTo1, cancellationToken1);
                return AsyncEnumerable.Empty<byte[]>();
            }
            var port = serviceEndpoints.GetPort()!.Port;
            var addresses = addressRing.Segment(streamId.Value.GetDjb2HashCode(), WriteReplicaCount);
            var readReplicaCount = addresses.Count.Clamp(0, ReadReplicaCount);

            DebugLog?.LogInformation("Read({Stream}): hitting [{Addresses}]", streamName, addresses.ToDelimitedString());
            var randomizedAddresses = addresses.Shuffle().Take(readReplicaCount);
            foreach (var address in randomizedAddresses) {
                DebugLog?.LogInformation("Read({Stream}): trying {Address}", streamName, address);
                using var client = await GetAudioStreamClient(kube, address, port, cancellationToken1).ConfigureAwait(false);
                var stream = await client.Read(streamId, skipTo1, cancellationToken1).ConfigureAwait(false);
                var result = await stream.IsNonEmpty(Clocks.CpuClock, ReadStreamWaitTimeout, cancellationToken1)
                    .ConfigureAwait(false);
                if (result.IsSome(out var s)) {
                    DebugLog?.LogInformation("Read({Stream}): found the stream on {Address}", streamName, address);
                    return s;
                }
            }
            DebugLog?.LogInformation("Read({Stream}): no stream found", streamName);
            if (AudioStreamServer.IsStreamExists(streamId))
                return await AudioStreamServer.Read(streamId, skipTo1, cancellationToken1);
            return AsyncEnumerable.Empty<byte[]>();
        }
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
        var memoized = stream.Memoize(cancellationToken);
        var isNotCompletedYet = true;
        var replicasLeft = WriteReplicaCount;
        var writeTasks = new HashSet<Task>();
        var addresses = endpointState.Value
            .GetAddressHashRing()
            .Segment(streamId.Value.GetDjb2HashCode(), replicasLeft);
        DebugLog?.LogInformation("Write({Stream}): hitting [{Addresses}]", streamName, addresses.ToDelimitedString());
        writeTasks.AddRange(addresses
            .Select(address => WriteToReplica(streamId,
                streamName,
                memoized,
                kube,
                address,
                port,
                cancellationToken)));
        var retryCount = 0;
        while (isNotCompletedYet && retryCount <= WriteAttemptCount) {
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
                writeTasks.AddRange(addresses
                    .Select(address => WriteToReplica(streamId,
                        streamName,
                        memoized,
                        kube,
                        address,
                        port,
                        cancellationToken)));
                retryCount++;
            }
            else
                isNotCompletedYet = writeTasks.Any(t => !t.IsCompleted);
        }
        if (writeTasks.Count > 0)
            await Task.WhenAll(writeTasks).ConfigureAwait(false);
    }

    public void Dispose()
    { }

    private async Task WriteToReplica(
        Symbol streamId,
        string streamName,
        AsyncMemoizer<byte[]> memoized,
        Kube kube,
        string address,
        int port,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("WriteToReplica({Stream}): writing to {Address} started", streamName, address);
        using var client = await GetAudioStreamClient(kube, address, port, cancellationToken).ConfigureAwait(false);
        try {
            await client.Write(streamId, memoized.Replay(cancellationToken), cancellationToken).ConfigureAwait(false);
            DebugLog?.LogInformation("WriteToReplica({Stream}): done writing to {Address}", streamName, address);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "WriteToReplica({Stream}): failed writing to {Address}", streamName, address);
        }
    }

    private async Task<IAudioStreamServer> GetAudioStreamClient(
        Kube kube, string address, int port, CancellationToken cancellationToken)
        => OrdinalEquals(address, kube.PodIP) && !kube.IsEmulated
            ? AudioStreamServer.SkipDispose()
            : await AudioHubBackendClientFactory.GetAudioStreamClient(address, port, cancellationToken).ConfigureAwait(false);
}
