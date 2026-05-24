using System.Buffers;
using ActualChat.Queues.Internal;
using ActualLab.Locking;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace ActualChat.Queues.Nats;

public sealed class NatsQueueProcessor : ShardQueueProcessor<NatsQueues.Options, NatsQueues, INatsJSMsg<IMemoryOwner<byte>>>
{
    // Wire format:
    // - v2: 1-byte version + MemoryPack-encoded uuid + TypeDecorating(MemoryPack)-encoded command
    // - v3: 1-byte version + MessagePack-encoded uuid + TypeDecorating(MessagePack)-encoded command
    // We always write v3, but still read v2 to drain in-flight messages from older publishers.
    private const byte FormatVersion = 3;
    private static readonly byte[] FormatVersionBytes = [FormatVersion];
    private static IByteSerializer SerializerV2 => Serializers.MemoryPack;
    private static IByteSerializer SerializerV3 => Serializers.MessagePack;
    private static IByteSerializer TypeDecoratingSerializerV2 => Serializers.MemoryPackTypeDecorating;
    private static IByteSerializer TypeDecoratingSerializerV3 => Serializers.MessagePackTypeDecorating;

    private readonly AsyncLockSet<int> _getStreamLocks = new();
    private readonly AsyncLockSet<int> _getConsumerLock = new();
    private readonly ConcurrentDictionary<int, INatsJSStream> _streams = new ();
    private readonly ConcurrentDictionary<int, INatsJSConsumer> _consumers = new ();
    private readonly string _instancePrefix;

    private IMeshLocks ActionLocks { get; }

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
        ActionLocks = queues.ActionLocks.WithKeyPrefix(ShardScheme.Name);
        _instancePrefix = queues.NatsSettings.InstancePrefix;
    }

    public override async Task Enqueue(QueueShardRef queueShardRef, QueuedCommand queuedCommand, CancellationToken cancellationToken = default)
    {
        if (queueShardRef.QueueRef != QueueRef)
            throw new ArgumentOutOfRangeException(nameof(queueShardRef));

        var shardIndex = queueShardRef.GetShardIndex();
        await GetStream(shardIndex, cancellationToken).ConfigureAwait(false);
        var context = new NatsJSContext(Connection);
        var buffer = new ArrayPoolBuffer<byte>(ArrayPools.SharedBytePool, 1024);
        try {
            Serialize(buffer, queuedCommand);
            var subjectName = GetSubjectName(shardIndex, Queues.GetTopic(queuedCommand.UntypedCommand));
            var headers = ReferenceEquals(queuedCommand.Headers, null)
                ? null
                : new NatsHeaders(queuedCommand.Headers.ToDictionary());
            var response = await context.PublishAsync(subjectName,
                    buffer.WrittenMemory,
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

    protected override async Task OnRun(ShardOwnership shardOwnership, CancellationToken cancellationToken)
    {
        var shardIndex = shardOwnership.ShardIndex;
        DebugLog?.LogDebug("[{ShardScheme}-S{ShardIndex}] OnRun started", ShardScheme.Name, shardIndex);

        var concurrencyLevel = Settings.ConcurrencyLevel;
        var buffer = Channel.CreateBounded<(INatsJSMsg<IMemoryOwner<byte>> Message, QueuedCommand Command)>(
            new BoundedChannelOptions(concurrencyLevel) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
            });

        using var gracefulStopCts = cancellationToken.CreateDelayedTokenSource(Settings.GracefulStopDelay);
        var gracefulStopToken = gracefulStopCts.Token;
        var mustLogExit = true;
        try {
            // ReSharper disable once PossiblyMistakenUseOfCancellationToken
            var processTask = buffer.ProcessConcurrently(concurrencyLevel, ProcessImpl, gracefulStopToken);
            var copyToBufferTask = CopyToBuffer();
            await Task.WhenAll(copyToBufferTask, processTask).ConfigureAwait(false);
        }
        // ReSharper disable once PossiblyMistakenUseOfCancellationToken
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken) && !e.IsCancellationOf(gracefulStopToken)) {
            Log.LogError(e, "[{ShardScheme}-S{ShardIndex}] OnRun failed", ShardScheme.Name, shardIndex);
            mustLogExit = false;
        }
        finally {
            gracefulStopCts.CancelAndDisposeSilently();
            if (mustLogExit)
                DebugLog?.LogDebug("[{ShardScheme}-S{ShardIndex}] OnRun completed", ShardScheme.Name, shardIndex);
        }
        return;

        async Task CopyToBuffer()
        {
            Exception? error = null;
            try {
                // ReSharper disable once PossiblyMistakenUseOfCancellationToken
                var consumer = await GetConsumer(shardIndex, cancellationToken).ConfigureAwait(false);
                DebugLog?.LogDebug(
                    "[{ShardScheme}-S{ShardIndex}] Got consumer='{Consumer}', stream='{Stream}'",
                    ShardScheme.Name, shardIndex, consumer.Info.Name, consumer.Info.StreamName);

                while (!cancellationToken.IsCancellationRequested) {
                    var messages = consumer.FetchAsync<IMemoryOwner<byte>>(
                        opts: new NatsJSFetchOpts {
                            MaxMsgs = Math.Min(8 * concurrencyLevel, consumer.Info.Config.MaxBatch),
                            Expires = Settings.FetchTimeout,
                            IdleHeartbeat = Settings.FetchTimeout / 3,
                        },
                        // ReSharper disable once PossiblyMistakenUseOfCancellationToken
                        cancellationToken: cancellationToken);
                    MarkStarted();

                    var count = 0;
                    await foreach (var message in messages.ConfigureAwait(false)) {
                        gracefulStopToken.ThrowIfCancellationRequested(); // We cancel it on DI container disposal

                        count++;
                        QueuedCommand? queuedCommand;
                        try {
                            queuedCommand = Deserialize(message, Queues);
                        }
                        catch (Exception e) when (!e.IsCancellationOf(gracefulStopToken)) {
                            Log.LogError(e,
                                "[{ShardScheme}-S{ShardIndex}] Couldn't deserialize the message",
                                ShardScheme.Name, shardIndex);
                            await MarkFailed(shardIndex, message, null, e, gracefulStopToken).ConfigureAwait(false);
                            message.Data.DisposeSilently();
                            continue;
                        }

                        await buffer.Writer.WriteAsync((message, queuedCommand), gracefulStopToken).ConfigureAwait(false);
                    }
                    if (count > 0)
                        DebugLog?.LogDebug(
                            "[{ShardScheme}-S{ShardIndex}] Pulled {Count} messages",
                            ShardScheme.Name, shardIndex, count);
                }
            }
            catch (Exception e) {
                error = e;
                throw;
            }
            finally {
                buffer.Writer.TryComplete(error);
            }
        }

        async ValueTask ProcessImpl(
            (INatsJSMsg<IMemoryOwner<byte>> Message, QueuedCommand Command) item,
            CancellationToken ct)
        {
            var (message, queuedCommand) = item;
            try {
                if (ct.IsCancellationRequested)
                    return; // We want the batch processing to complete successfully

                // NOTE: We already deserialized in pump, so we avoid doing it again.
                await Process(shardIndex, message, queuedCommand, Stopwatch.StartNew(), ct).ConfigureAwait(false);
            }
            catch (Exception e)  {
                if (e is ObjectDisposedException or TargetException && Services.IsDisposedOrDisposing()) {
                    // NOTE(AY): NatsQueueProcessor is sometimes disposed ~ at the very end
                    // of container disposal, and thus it retries many times to process
                    // queued events, even though it's already impossible, coz the Commander
                    // can't create scope for any new command it runs, because the container
                    // is already disposed.
                    // TargetException can be thrown by MethodCommandHandler on an attempt to
                    // invoke a method of an already disposed service.
                    // ReSharper disable once AccessToDisposedClosure
                    gracefulStopCts.CancelAndDisposeSilently();
                }

                // We want ForEachAsync to complete successfully, so we suppress all exceptions here.
                // There is nothing to log here, coz Process logs its own errors.
            }
            finally {
                message.Data.DisposeSilently();
            }
        }
    }

    // MarkXxx

    protected override Task MarkCompleted(
        int shardIndex, INatsJSMsg<IMemoryOwner<byte>> message, QueuedCommand? command,
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
        int shardIndex, INatsJSMsg<IMemoryOwner<byte>> message, QueuedCommand? command, Exception? exception,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(exception, "[{ShardIndex}]: Marking failed {Kind} command #{Uuid} {Command}",
            shardIndex,
            command?.UntypedCommand.GetKind(),
            command?.Uuid,
            command?.UntypedCommand);
        return message.NakAsync(new AckOpts { DoubleAck = true }, cancellationToken).AsTask();
    }

    protected override Task MarkPostponed(
        int shardIndex, INatsJSMsg<IMemoryOwner<byte>> message, QueuedCommand command, TimeSpan delay,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("[{ShardIndex}]: Marking postponed {Kind} command #{Uuid} {Command} for {Time}",
            shardIndex,
            command.UntypedCommand.GetKind(),
            command.Uuid,
            command.UntypedCommand,
            delay);
        return message.NakAsync(new AckOpts { DoubleAck = true, NakDelay = delay }, cancellationToken).AsTask();
    }

    // GetXxxName/Filter/Config

    private string GetStreamName(int shardIndex)
        => Settings.UseStreamPerShard
            ? $"{_instancePrefix}{QueueRef.ShardScheme.Name}-S{shardIndex.Format()}"
            : $"{_instancePrefix}{QueueRef.ShardScheme.Name}";

    private string GetSubjectName(int shardIndex, Symbol topic)
        => Settings.UseStreamPerShard
            ? $"{_instancePrefix}{QueueRef.ShardScheme.Name}-S{shardIndex.Format()}.{topic.Value.NullIfEmpty() ?? "_"}"
            : $"{_instancePrefix}{QueueRef.ShardScheme.Name}.S{shardIndex.Format()}.{topic.Value.NullIfEmpty() ?? "_"}";

    private string GetConsumerName(int shardIndex)
        => Settings.UseStreamPerShard
            ? $"{_instancePrefix}{QueueRef.ShardScheme.Name}-S{shardIndex.Format()}"
            : $"{_instancePrefix}{QueueRef.ShardScheme.Name}.S{shardIndex.Format()}";

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
            AckWait = TimeSpan.FromSeconds(30) +
                (ShardScheme.HasFlags(ShardSchemeFlags.SlowQueue)
                ? QueuesExt.SlowQueueTimeout
                : QueuesExt.QueueTimeout),
            MaxAckPending = Settings.MaxPendingCount,
            MaxBatch = 64,
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
        var lockHolder = await ActionLocks.Lock($"{nameof(CreateStream)}.{streamName}", cancellationToken).ConfigureAwait(false);
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
        var lockHolder = await ActionLocks
            .Lock($"{nameof(CreateOrUpdateConsumer)}.{consumerName}", cancellationToken)
            .ConfigureAwait(false);
        await using var _ = lockHolder.ConfigureAwait(false);
        var linkedCts = cancellationToken.LinkWith(lockHolder.StopToken);

        var config = GetConsumerConfig(shardIndex, consumerName);
        return await stream.CreateOrUpdateConsumerAsync(config, linkedCts.Token).ConfigureAwait(false);
    }

    // Serialization

    private static QueuedCommand Deserialize(INatsJSMsg<IMemoryOwner<byte>> message, IQueues queues)
    {
        var data = message.Data;
        if (data == null)
            throw StandardError.Internal("No data to deserialize.");

        var dataMemory = data.Memory;
        var formatVersion = dataMemory.Span[0];
        var (serializer, typeDecoratingSerializer) = formatVersion switch {
            2 => (SerializerV2, TypeDecoratingSerializerV2),
            3 => (SerializerV3, TypeDecoratingSerializerV3),
            _ => throw StandardError.Internal($"Unsupported format version: {formatVersion}."),
        };
        var uuid = (string)serializer.Read(dataMemory[1..], typeof(string), out var uuidLength)!;
        var command = (ICommand)typeDecoratingSerializer.Read(dataMemory[(1 + uuidLength)..], typeof(ICommand), out _)!;
        return QueuedCommand.NewUntyped(command, uuid, message.Headers?.AsReadOnly(), queues);
    }

    private static void Serialize(ArrayPoolBuffer<byte> buffer, QueuedCommand queuedCommand)
    {
        var command = queuedCommand.UntypedCommand;
        buffer.Write(FormatVersionBytes);
        SerializerV3.Write(buffer, queuedCommand.Uuid, typeof(string));
        TypeDecoratingSerializerV3.Write(buffer, command, command.GetType()); // Command itself
    }
}
