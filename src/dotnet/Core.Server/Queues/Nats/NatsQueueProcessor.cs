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
    private const byte Version = 1;
    private static readonly byte[] VersionBytes = [Version];
    private static readonly byte[] SupportedVersions = [1];
    private static readonly TypeDecoratingByteSerializer Serializer = new(MemoryPackByteSerializer.Default);

    private readonly AsyncLockSet<int> _getStreamLocks = new();
    private readonly AsyncLockSet<int> _getConsumerLock = new();
    private readonly ConcurrentDictionary<int, INatsJSStream> _streams = new ();
    private readonly ConcurrentDictionary<int, INatsJSConsumer> _consumers = new ();
    private readonly string _instancePrefix;
    private NatsConnection? _connection;

    private IMeshLocks ActionLocks { get; }

    private NatsConnection Connection {
        get {
            if (_connection != null)
                return _connection;

            lock (Lock)
                return _connection = Services.GetRequiredService<NatsConnection>();
        }
    }

    public NatsQueueProcessor(NatsQueues.Options settings, NatsQueues queues, QueueRef queueRef)
        : base(settings, queues, queueRef)
    {
        ActionLocks = GetMeshLocks(nameof(ActionLocks));
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
            var response = await context.PublishAsync(subjectName,
                    buffer,
                    opts: new NatsJSPubOpts { MsgId = queuedCommand.Id.ToString() },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (response.Error is { } error) {
                Log.LogError("NATS write failed: Code={Code}, ErrCode={ErrCode}, Description={Description}",
                    error.Code,
                    error.ErrCode,
                    error.Description);
                throw StandardError.External($"NATS write failed: Code={error.Code}, ErrCode={error.ErrCode}");
            }
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
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        using var gracefulStopCts = cancellationToken.CreateDelayedTokenSource(Settings.ProcessCancellationDelay);
        var gracefulStopToken = gracefulStopCts.Token;

        var consumer = await GetConsumer(shardIndex, cancellationToken).ConfigureAwait(false);
        var messages = consumer.ConsumeAsync<IMemoryOwner<byte>>(
            opts: new NatsJSConsumeOpts { MaxMsgs = 10 },
            cancellationToken: cancellationToken);

        var parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = Settings.ConcurrencyLevel,
            CancellationToken = cancellationToken,
        };
        await Parallel.ForEachAsync(messages, parallelOptions, HandleMessage).ConfigureAwait(false);
        return;

        async ValueTask HandleMessage(NatsJSMsg<IMemoryOwner<byte>> message, CancellationToken _) {
            try {
                StopToken.ThrowIfCancellationRequested();
                await Process(shardIndex, message, gracefulStopToken).ConfigureAwait(false);
            }
            finally {
                message.Data.DisposeSilently();
            }
        }
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
        => message.AckAsync(new AckOpts { DoubleAck = true }, cancellationToken).AsTask();

    protected override Task MarkFailed(
        int shardIndex, NatsJSMsg<IMemoryOwner<byte>> message, QueuedCommand? command, Exception? exception,
        CancellationToken cancellationToken)
        => message.NakAsync(new AckOpts { DoubleAck = true }, default, cancellationToken).AsTask();

    protected override Task MarkPostponed(
        int shardIndex, NatsJSMsg<IMemoryOwner<byte>> message, QueuedCommand queuedCommand, TimeSpan delay,
        CancellationToken cancellationToken)
        => message.NakAsync(new AckOpts { DoubleAck = true }, delay, cancellationToken).AsTask();

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
            MaxAckPending = 100,
            MaxBatch = 10,
            SampleFreq = "20%",
        };

    // Get/CreateStream

    private async ValueTask<INatsJSStream> GetStream(int shardIndex, CancellationToken cancellationToken)
    {
        if (!Settings.UseStreamPerShard)
            shardIndex = 0;

        // Double-check locking
        if (_streams.TryGetValue(shardIndex, out var stream)) return stream;
        using var releaser = await _getStreamLocks.Lock(shardIndex, cancellationToken).ConfigureAwait(false);
        if (_streams.TryGetValue(shardIndex, out stream)) return stream;

        var streamName = GetStreamName(shardIndex);
        var context = new NatsJSContext(Connection);
        var retryCount = 0;
        while (stream == null) {
            try {
                try {
                    stream = await context
                        .GetStreamAsync(streamName, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (NatsJSApiException e) when (e.Error.Code == 404) {
                    stream = await CreateStream(shardIndex, context, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is TimeoutException or NatsJSApiNoResponseException) {
                if (retryCount++ > 3)
                    throw;

                Log.LogWarning(e, $"{nameof(GetStream)}: error getting stream - timeout");
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
                Log.LogWarning(e, $"{nameof(GetConsumer)}: error getting consumer - timeout");
                var delay = Random.Shared.Next(100, 250);
                await Services.Clocks().SystemClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (NatsJSApiNoResponseException e) when (retryCount++ <= 3) {
                Log.LogWarning(e, $"{nameof(GetConsumer)}: error getting consumer - no response");
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
        if (!SupportedVersions.Contains(version))
            throw StandardError.Internal($"Unsupported command version: {version}.");

        var id = new Ulid(dataSpan[1..17]);
        var command = Serializer.Read<ICommand>(dataMemory[17..]);
        return QueuedCommand.NewUntyped(command, id);
    }

    private static void Serialize(ArrayPoolBuffer<byte> buffer, QueuedCommand queuedCommand)
    {
        buffer.Write(VersionBytes); // Version (1 byte)
        queuedCommand.Id.TryWriteBytes(buffer.GetSpan(16)); // Ulid - as 16-byte sequence
        buffer.Advance(16);
        var command = queuedCommand.UntypedCommand;
        var commandType = command.GetType();
        Serializer.Write(buffer, command, commandType); // Command itself
    }
}
