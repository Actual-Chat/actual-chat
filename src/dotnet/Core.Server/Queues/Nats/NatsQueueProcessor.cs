using System.Buffers;
using ActualChat.Mesh;
using ActualChat.Queues.Internal;
using ActualLab.IO;
using ActualLab.Locking;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace ActualChat.Queues.Nats;

public sealed class NatsQueueProcessor : ShardQueueProcessor<NatsQueues.Options, NatsQueues, NatsJSMsg<IMemoryOwner<byte>>>
{
    private const byte Version = 2;
    private static readonly byte[] VersionBytes = [Version];
    private static readonly IByteSerializer Serializer = MemoryPackByteSerializer.Default;
    private static readonly IByteSerializer TypeDecoratingSerializer
        = new TypeDecoratingByteSerializer(MemoryPackByteSerializer.Default);

    private readonly AsyncLockSet<int> _getStreamLocks = new();
    private readonly AsyncLockSet<int> _getConsumerLock = new();
    private readonly ConcurrentDictionary<int, INatsJSStream> _streams = new ();
    private readonly ConcurrentDictionary<int, INatsJSConsumer> _consumers = new ();
    private readonly string _instancePrefix;

    private IMeshLocks ActionLocks { get; }

    [field: AllowNull, MaybeNull]
    private NatsConnection Connection {
        get {
            if (field != null)
                return field;

            lock (Lock)
                return field = Services.GetRequiredService<NatsConnection>();
        }
    }

    public NatsQueueProcessor(NatsQueues.Options settings, NatsQueues queues, QueueRef queueRef)
        : base(settings, queues, queueRef)
    {
        ActionLocks = ShardScheduler.Owner.GetShardLocks(ShardScheme, nameof(ActionLocks));
        _instancePrefix = queues.NatsSettings.InstancePrefix;
    }

    public override async Task Enqueue(QueueShardRef queueShardRef, QueuedCommand queuedCommand, CancellationToken cancellationToken = default)
    {
        RequireValid(queueShardRef.QueueRef);
        var shardIndex = queueShardRef.GetShardIndex();
        await GetStream(shardIndex, cancellationToken).ConfigureAwait(false);
        var context = new NatsJSContext(Connection);
        var buffer = new ArrayPoolBuffer<byte>();
        try {
            Serialize(buffer, queuedCommand);
            var subjectName = GetSubjectName(shardIndex, Queues.GetTopic(queuedCommand.UntypedCommand));
            var headers = ReferenceEquals(queuedCommand.Headers, null)
                ? null
                : new NatsHeaders(queuedCommand.Headers.ToDictionary(StringComparer.Ordinal));
            var response = await context.PublishAsync(subjectName,
                    buffer,
                    opts: new NatsJSPubOpts { MsgId = queuedCommand.Uuid },
                    headers: headers,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (response.Error is { } error) {
                Log.LogError(
                    "NATS write failed: Code={Code}, ErrCode={ErrCode}, Description={Description}, {Kind} command #{Uuid} {Command}",
                    error.Code,
                    error.ErrCode,
                    error.Description,
                    queuedCommand.UntypedCommand.GetKind(),
                    queuedCommand.Uuid,
                    queuedCommand.UntypedCommand);
                throw StandardError.External($"NATS write failed: Code={error.Code}, ErrCode={error.ErrCode}");
            }
            DebugLog?.LogDebug(
                "NATS write succeeded: {Kind} command #{Uuid} {Command} to '{Stream}' with domain '{Domain}'",
                queuedCommand.UntypedCommand.GetKind(),
                queuedCommand.Uuid,
                queuedCommand.UntypedCommand,
                response.Stream,
                response.Domain);
        }
        catch (Exception e) when (e is not ExternalError) {
            Log.LogError(e, "NATS write failed");
            throw;
        }
        finally {
            buffer.Dispose();
        }
    }

    public override async Task Purge(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        for (var shardIndex = 0; shardIndex < ShardScheme.ShardCount; shardIndex++) {
            var stream = await GetStream(shardIndex, cancellationToken).ConfigureAwait(false);
            var purgeRequest = new StreamPurgeRequest() {
                Filter = GetConsumerFilter(shardIndex),
            };
            tasks.Add(stream.PurgeAsync(purgeRequest, cancellationToken).AsTask());
            DebugLog?.LogDebug("NATS purge requested for stream #{Stream}", stream.Info.Config.Name);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("OnRun: ShardScheme={ShardScheme}, ShardIndex={ShardIndex}", ShardScheme, shardIndex);

        var expireIn = Settings.IdleTimeout.ToRandom(0.25);
        using var stopCts = cancellationToken.CreateLinkedTokenSource();
        var stopToken = stopCts.Token;

        while (!stopToken.IsCancellationRequested) {
            // retry pull until cancellation is requested
            var consumer = await GetConsumer(shardIndex, stopToken).ConfigureAwait(false);
            DebugLog?.LogDebug(
                "NATS: pulling messages from consumer='{Consumer}' stream='{Stream}' shard='{ShardIndex}'",
                consumer.Info.Name,
                consumer.Info.StreamName,
                shardIndex);
            var batchSize = ShardScheme.HasFlags(ShardSchemeFlags.SlowQueue)
                ? 2
                : consumer.Info.Config.MaxBatch;
            var fetchExpiration = expireIn.Next();
            var messages = consumer.FetchAsync<IMemoryOwner<byte>>(
                opts: new NatsJSFetchOpts {
                    MaxMsgs = batchSize,
                    Expires = fetchExpiration,
                    IdleHeartbeat = fetchExpiration / 2,
                },
                cancellationToken: stopToken);

            MarkStarted();
            var degreeOfParallelism = ShardScheme.DegreeOfParallelism ?? Settings.ConcurrencyLevel;
            var parallelOptions = new ParallelOptions {
                MaxDegreeOfParallelism = degreeOfParallelism,
                CancellationToken = stopToken,
            };

            var handledCount = 0;
            await Parallel
                .ForEachAsync(messages, parallelOptions, HandleMessage)
                .SilentAwait(false); // We swallow all exceptions here
            DebugLog?.LogDebug(
                "NATS: handled {Count} messages from consumer='{Consumer}' stream='{Stream}' shard='{ShardIndex}'",
                handledCount,
                consumer.Info.Name,
                consumer.Info.StreamName,
                shardIndex);
            continue;

            async ValueTask HandleMessage(NatsJSMsg<IMemoryOwner<byte>> message, CancellationToken cancellationToken1) {
                try {
                    await Process(shardIndex, message, cancellationToken1).ConfigureAwait(false);
                    Interlocked.Increment(ref handledCount);
                }
                catch (ObjectDisposedException) {
                    // NOTE(AY): NatsQueueProcessor is sometimes disposed ~ at the very end
                    // of container disposal, and thus it retries many times to process
                    // queued events, even though it's already impossible, coz the Commander
                    // can't create scope for any new command it runs, because the container
                    // is already disposed.
                    // So here we detect this & instantly abort the message reader.
                    if (Services.IsDisposedOrDisposing())
                        // ReSharper disable once AccessToDisposedClosure
                        stopCts.CancelAndDisposeSilently();
                    throw;
                }
                finally {
                    message.Data.DisposeSilently();
                }
            }
        }
        stopToken.ThrowIfCancellationRequested();
    }

    // Private methods

    private QueueRef RequireValid(QueueRef queueRef)
        => queueRef == QueueRef ? queueRef
            : throw new ArgumentOutOfRangeException(nameof(queueRef),
                "Can't use provided QueueRef with the current IQueueProcessor.");

    // MarkXxx

    protected override Task MarkCompleted(
        int shardIndex, NatsJSMsg<IMemoryOwner<byte>> message, QueuedCommand? command,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("[{ShardIndex}]: Marking completed {Kind} command #{Uuid} {Command}",
            shardIndex,
            command?.UntypedCommand.GetKind(),
            command?.Uuid,
            command?.UntypedCommand);
        return message.AckAsync(new AckOpts { DoubleAck = true }, cancellationToken).AsTask();
    }

    protected override Task MarkFailed(
        int shardIndex, NatsJSMsg<IMemoryOwner<byte>> message, QueuedCommand? command, Exception? exception,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(exception, "[{ShardIndex}]: Marking failed {Kind} command #{Uuid} {Command}",
            shardIndex,
            command?.UntypedCommand.GetKind(),
            command?.Uuid,
            command?.UntypedCommand);
        return message.NakAsync(new AckOpts { DoubleAck = true }, default, cancellationToken).AsTask();
    }

    protected override Task MarkPostponed(
        int shardIndex, NatsJSMsg<IMemoryOwner<byte>> message, QueuedCommand command, TimeSpan delay,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("[{ShardIndex}]: Marking postponed {Kind} command #{Uuid} {Command} for {Time}",
            shardIndex,
            command.UntypedCommand.GetKind(),
            command.Uuid,
            command.UntypedCommand,
            delay);
        return message.NakAsync(new AckOpts { DoubleAck = true }, delay, cancellationToken).AsTask();
    }

    // GetXxxName/Filter/Config

    private string GetStreamName(int shardIndex)
        => Settings.UseStreamPerShard
            ? $"{_instancePrefix}{QueueRef.ShardScheme.Id}-S{shardIndex.Format()}"
            : $"{_instancePrefix}{QueueRef.ShardScheme.Id}";

    private string GetSubjectName(int shardIndex, Symbol topic)
        => Settings.UseStreamPerShard
            ? $"{_instancePrefix}{QueueRef.ShardScheme.Id}-S{shardIndex.Format()}.{topic.Value.NullIfEmpty() ?? "_"}"
            : $"{_instancePrefix}{QueueRef.ShardScheme.Id}.S{shardIndex.Format()}.{topic.Value.NullIfEmpty() ?? "_"}";

    private string GetConsumerName(int shardIndex)
        => Settings.UseStreamPerShard
            ? $"{_instancePrefix}{QueueRef.ShardScheme.Id}-S{shardIndex.Format()}"
            : $"{_instancePrefix}{QueueRef.ShardScheme.Id}.S{shardIndex.Format()}";

    private string GetConsumerFilter(int shardIndex)
        => $"{GetConsumerName(shardIndex)}.>";

    private StreamConfig GetStreamConfig(int shardIndex, string streamName)
        => new (streamName, [$"{streamName}.>"]) {
            MaxMsgs = Queues.Settings.MaxQueueSize,
            Compression = StreamConfigCompression.S2,
            Storage = StreamConfigStorage.File,
            NumReplicas = Queues.Settings.ReplicaCount,
            Discard = StreamConfigDiscard.Old,
            Retention = StreamConfigRetention.Workqueue,
            AllowDirect = true,
        };

    private ConsumerConfig GetConsumerConfig(int shardIndex, string consumerName)
        => new (consumerName) {
            MaxDeliver = Settings.MaxTryCount,
            FilterSubject = GetConsumerFilter(shardIndex),
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = ShardScheme.HasFlags(ShardSchemeFlags.SlowQueue)
                ? TimeSpan.FromMinutes(15)
                : TimeSpan.FromSeconds(15),
            MaxAckPending = Settings.MaxPendingCount,
            MaxBatch = 10,
            SampleFreq = "20%",
        };

    // Get/CreateStream

    private async ValueTask<INatsJSStream> GetStream(int shardIndex, CancellationToken cancellationToken)
    {
        if (!Settings.UseStreamPerShard)
            shardIndex = 0;

        // Double-check locking
        if (_streams.TryGetValue(shardIndex, out var stream))
            return stream;

        using var releaser = await _getStreamLocks.Lock(shardIndex, cancellationToken).ConfigureAwait(false);
        if (_streams.TryGetValue(shardIndex, out stream))
            return stream;

        var streamName = GetStreamName(shardIndex);
        var context = new NatsJSContext(Connection);
        var retryCount = 0;
        while (stream == null) {
            try {
                try {
                    DebugLog?.LogDebug("Attempting to get stream {Stream}", streamName);
                    stream = await context
                        .GetStreamAsync(streamName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (NatsJSApiException e) when (e.Error.Code == 404) {
                    stream = await CreateStream(shardIndex, context, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is TimeoutException or NatsJSApiNoResponseException) {
                if (retryCount++ > 5)
                    throw;

                Log.LogWarning(e, $"{nameof(GetStream)}: error getting stream {{StreamName}} - timeout", streamName);
                var delay = Random.Shared.Next(100, 250);
                await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        _streams.TryAdd(shardIndex, stream);
        return stream;
    }

    private async Task<INatsJSStream> CreateStream(
        int shardIndex,
        NatsJSContext context,
        CancellationToken cancellationToken)
    {
        var streamName = GetStreamName(shardIndex);
        var lockHolder = await ActionLocks.Lock($"{nameof(CreateStream)}({streamName})", "", cancellationToken).ConfigureAwait(false);
        await using var _ = lockHolder.ConfigureAwait(false);
        var lockCts = cancellationToken.LinkWith(lockHolder.StopToken);

        var config = GetStreamConfig(shardIndex, streamName);
        return await context.CreateStreamAsync(config, lockCts.Token).ConfigureAwait(false);
    }

    // Get/CreateConsumer

    private async ValueTask<INatsJSConsumer> GetConsumer(int shardIndex, CancellationToken cancellationToken)
    {
        // Double-check locking
        if (_consumers.TryGetValue(shardIndex, out var consumer)) return consumer;
        using var releaser = await _getConsumerLock.Lock(shardIndex, cancellationToken).ConfigureAwait(false);
        if (_consumers.TryGetValue(shardIndex, out consumer)) return consumer;

        var consumerName = GetConsumerName(shardIndex);
        var stream = await GetStream(shardIndex, cancellationToken).ConfigureAwait(false);
        var retryCount = 0;
        while (consumer == null) {
            try {
                consumer = await stream.GetConsumerAsync(consumerName, cancellationToken).ConfigureAwait(false);
            }
            catch (NatsJSApiException e) when (e.Error.Code == 404) {
                consumer = await CreateOrUpdateConsumer(shardIndex, stream, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException e) when (retryCount++ <= 3) {
                Log.LogWarning(e, $"{nameof(GetConsumer)}: error getting consumer {{ConsumerName}} - timeout", consumerName);
                var delay = Random.Shared.Next(100, 250);
                await Services.Clocks().SystemClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (NatsJSApiNoResponseException e) when (retryCount++ <= 3) {
                Log.LogWarning(e, $"{nameof(GetConsumer)}: error getting consumer {{ConsumerName}} - no response", consumerName);
                var delay = Random.Shared.Next(100, 250);
                await Services.Clocks().SystemClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        return _consumers.GetOrAdd(shardIndex, consumer);
    }

    private async Task<INatsJSConsumer> CreateOrUpdateConsumer(
        int shardIndex,
        INatsJSStream stream,
        CancellationToken cancellationToken)
    {
        var consumerName = GetConsumerName(shardIndex);
        var lockHolder = await ActionLocks.Lock($"{nameof(CreateOrUpdateConsumer)}({consumerName})", "", cancellationToken).ConfigureAwait(false);
        await using var _ = lockHolder.ConfigureAwait(false);
        var lockCts = cancellationToken.LinkWith(lockHolder.StopToken);

        var config = GetConsumerConfig(shardIndex, consumerName);
        return await stream.CreateOrUpdateConsumerAsync(config, lockCts.Token).ConfigureAwait(false);
    }

    // Serialization

    protected override QueuedCommand Deserialize(NatsJSMsg<IMemoryOwner<byte>> message)
    {
        var data = message.Data;
        if (data == null)
            throw StandardError.Internal("No data to deserialize.");

        var dataMemory = data.Memory;
        var dataSpan = dataMemory.Span;
        var version = dataSpan[0];

        switch (version) {
        case 1: {
            var id = new Ulid(dataSpan[1..17]);
            var command = (ICommand)TypeDecoratingSerializer.Read(dataMemory[17..], typeof(ICommand), out _)!;
            return QueuedCommand.NewUntyped(command, id.ToString(), message.Headers?.AsReadOnly());
        }
        case 2: {
            var ulid = (string)Serializer.Read(dataMemory[1..], typeof(string), out var ulidLength)!;
            var command = (ICommand)TypeDecoratingSerializer.Read(dataMemory[(1 + ulidLength)..], typeof(ICommand), out _)!;
            return QueuedCommand.NewUntyped(command, ulid, message.Headers?.AsReadOnly());
        }
        default:
            throw StandardError.Internal($"Unsupported command version: {version}.");
        }
    }

    private static void Serialize(ArrayPoolBuffer<byte> buffer, QueuedCommand queuedCommand)
    {
        var command = queuedCommand.UntypedCommand;
        buffer.Write(VersionBytes);
        Serializer.Write(buffer, queuedCommand.Uuid, typeof(string));
        TypeDecoratingSerializer.Write(buffer, command, command.GetType()); // Command itself
    }
}
