using System.Buffers;
using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualLab.IO;
using ActualLab.Locking;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace ActualChat.Commands;

public class NatsCommandQueue(QueueId queueId, NatsCommandQueues queues, IServiceProvider services) : ICommandQueue, ICommandQueueBackend
{
    private const string JetStreamName = "COMMANDS";
    private static readonly byte[] Version = [ (byte)CommandVersion.Version1 ];
    private static readonly HashSet<byte> SupportedVersions = [
        (byte)CommandVersion.Version1,
    ];

    private readonly ConcurrentDictionary<Ulid, NatsJSMsg<IMemoryOwner<byte>>> _commandsBeingProcessed = new ();
    private readonly object _lock = new ();

    private NatsConnection? _nats;
    private volatile INatsJSStream? _jetStream;
    private volatile INatsJSConsumer? _jetStreamConsumer;

    protected NatsConnection Nats => _nats ??= GetConnection();
    protected AsyncLock AsyncLock { get; } = new(LockReentryMode.CheckedPass);
    protected ILogger<NatsCommandQueue> Log { get; } = services.LogFor<NatsCommandQueue>();

    public QueueId QueueId { get; } = queueId;
    public NatsCommandQueues Queues { get; } = queues;
    public IServiceProvider Services { get; } = services;
    public NatsCommandQueues.Options Settings { get; } = services.GetKeyedService<NatsCommandQueues.Options>(queueId.HostRole.Id.Value)
        ?? services.GetRequiredService<NatsCommandQueues.Options>();

    public async Task Enqueue(QueuedCommand command, CancellationToken cancellationToken = default)
    {
        await EnsureStreamExists(cancellationToken).ConfigureAwait(false);

        var js = new NatsJSContext(Nats);
        var typedSerializer = TypeDecoratingByteSerializer.Default;
        try {
            using var bufferWriter = new ArrayPoolBuffer<byte>();
            var commandType = command.UntypedCommand.GetType();
            bufferWriter.Write(Version);

            // Write command.Id - Ulid as 16-byte sequence
            command.Id.TryWriteBytes(bufferWriter.GetSpan(16));
            bufferWriter.Advance(16);

            typedSerializer.Write(bufferWriter, command.UntypedCommand, commandType);
            var subject = BuildSubject(commandType);
            var ackResponse = await js.PublishAsync(subject,
                    bufferWriter,
                    opts: new NatsJSPubOpts { MsgId = command.Id.ToString() },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (ackResponse.Error is { } error) {
                Log.LogError("Error writing to NATS: Code={Code}, ErrCode={ErrCode}, Description={Description}",
                    error.Code,
                    error.ErrCode,
                    error.Description);
                throw StandardError.External($"Error writing to NATS: Code={error.Code}, ErrCode={error.ErrCode}");
            }
        }
        catch (Exception e) when(e is not ExternalError) {
            Log.LogError(e, "Error writing to NATS");
            throw;
        }
    }

    public virtual async IAsyncEnumerable<QueuedCommand> Read([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var jetStream = await EnsureStreamExists(cancellationToken).ConfigureAwait(false);
        var consumer = await EnsureConsumerExists(jetStream, cancellationToken).ConfigureAwait(false);
        var messages = consumer.ConsumeAsync<IMemoryOwner<byte>>(
            opts: new NatsJSConsumeOpts {
                MaxMsgs = 10,
            },
            cancellationToken: cancellationToken);
        await foreach (var message in messages.ConfigureAwait(false)) {
            if (message.Data == null)
                continue;

            yield return DeserializeMessage(message);
        }
    }

    public async ValueTask MarkCompleted(QueuedCommand command, CancellationToken cancellationToken)
    {
        if (!_commandsBeingProcessed.TryRemove(command.Id, out var message)) {
            var streamName = _jetStream?.Info.Config.Name;
            Log.LogWarning("MarkCompleted. Command has already been completed. Id={Id}, StreamName={StreamName}", command.Id, streamName);
            return;
        }

        await message.AckAsync(new AckOpts { DoubleAck = true }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask MarkFailed(QueuedCommand command, Exception? exception, CancellationToken cancellationToken)
    {
        if (!_commandsBeingProcessed.TryRemove(command.Id, out var message)) {
            var streamName = _jetStream?.Info.Config.Name;
            Log.LogWarning("MarkFailed. Command has already been completed. Id={Id}, StreamName={StreamName}", command.Id, streamName);
            return;
        }

        await message.NakAsync(new AckOpts { DoubleAck = true }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask Purge(CancellationToken cancellationToken)
    {
        var jetStream = await EnsureStreamExists(cancellationToken).ConfigureAwait(false);
        await jetStream.PurgeAsync(new StreamPurgeRequest(), cancellationToken).ConfigureAwait(false);
    }

    protected virtual string BuildJetStreamName()
        => $"{GetPrefix()}{JetStreamName}";

    protected virtual string BuildSubject(Type commandType)
        => $"{GetPrefix()}commands.{GetRoleString(QueueId.HostRole)}.{QueueId.ShardIndex}.{commandType.Name}";

    protected static string GetRoleString(HostRole hostRole)
        => hostRole == HostRole.BackendServer
            ? "backend"
            : hostRole.Id.Value.Replace(HostRole.BackendServer.Id.Value, "", StringComparison.OrdinalIgnoreCase);

    protected QueuedCommand DeserializeMessage(NatsJSMsg<IMemoryOwner<byte>> message)
    {
        var typedSerializer = TypeDecoratingByteSerializer.Default;
        var messageMemory = message.Data!.Memory;
        var version = messageMemory.Span[0];
        if (!SupportedVersions.Contains(version))
            throw StandardError.NotSupported($"CommandVersion={version} is not supported.");

        var id = new Ulid(messageMemory.Span[1..17]);
        var command = typedSerializer.Read<ICommand>(messageMemory[17..]);
        var queuedCommand = QueuedCommand.FromCommand(id, command);
        if (_commandsBeingProcessed.TryAdd(queuedCommand.Id, message)) // double check duplicates - we have already handled them at the NATS stream
            return queuedCommand;

        message.Data.DisposeSilently(); // release buffer
        return queuedCommand;
    }

    protected NatsConnection GetConnection()
    {
        lock (_lock)
            return _nats = Services.GetRequiredService<NatsConnection>();
    }

    protected async ValueTask<INatsJSStream> EnsureStreamExists(CancellationToken cancellationToken)
    {
        if (_jetStream != null)
            return _jetStream;

        using var releaser = await AsyncLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        if (_jetStream != null)
            return _jetStream;

        var js = new NatsJSContext(Nats);
        var retryCount = 0;
        var jetStreamName = BuildJetStreamName();
        while (_jetStream == null)
            try {
                var jetStream = await js
                    .GetStreamAsync(jetStreamName, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _jetStream = jetStream;

            }
            catch (NatsJSApiException e) when (e.Error.Code == 404) {
                _jetStream = await CreateJetStream(js, jetStreamName, cancellationToken).ConfigureAwait(false);
            }
            catch (NatsJSApiNoResponseException e) {
                if (retryCount++ > 3)
                    throw;

                Log.LogWarning(e, "EnsureStreamExists: error getting stream");
                var delay = Random.Shared.Next(100, 250);
                await Services.Clocks().SystemClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

        return _jetStream;
    }

    protected virtual async ValueTask<INatsJSConsumer> EnsureConsumerExists(INatsJSStream jetStream, CancellationToken cancellationToken)
    {
        if (_jetStreamConsumer != null)
            return _jetStreamConsumer!;

        using var releaser = await AsyncLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        if (_jetStreamConsumer != null)
            return _jetStreamConsumer!;

        var consumerName = BuildConsumerName(QueueId.HostRole);
        var retryCount = 0;
        while (_jetStreamConsumer == null)
            try {
                var jetStreamConsumer = await jetStream.GetConsumerAsync(consumerName, cancellationToken).ConfigureAwait(false);
                _jetStreamConsumer = jetStreamConsumer;

            }
            catch (NatsJSApiException e) when (e.Error.Code == 404) {
                _jetStreamConsumer = await CreateConsumer(jetStream, consumerName, cancellationToken);
            }
            catch (NatsJSApiNoResponseException e) {
                if (retryCount++ > 3)
                    throw;

                Log.LogWarning(e, "EnsureStreamExists: error getting stream");
                var delay = Random.Shared.Next(100, 250);
                await Services.Clocks().SystemClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

        return _jetStreamConsumer;
    }

    protected virtual string BuildConsumerName(HostRole hostRole)
    {
        var consumerName = $"WORKER-{QueueId.ShardIndex}";
        return consumerName;
    }

    protected virtual async Task<INatsJSStream> CreateJetStream(NatsJSContext js, string jetStreamName, CancellationToken cancellationToken)
    {
        var meshLocks = Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(NatsCommandQueue));
        var lockHolder = await meshLocks.Lock(nameof(EnsureStreamExists), "", cancellationToken).ConfigureAwait(false);
        await using var _ = lockHolder.ConfigureAwait(false);
        var lockCts = cancellationToken.LinkWith(lockHolder.StopToken);

        var config = new StreamConfig(jetStreamName, new[] { $"{GetPrefix()}commands.>" }) {
            MaxMsgs = Queues.Settings.MaxQueueSize,
            Compression = StreamConfigCompression.S2,
            Storage = StreamConfigStorage.File,
            NumReplicas = Queues.Settings.ReplicaCount,
            Discard = StreamConfigDiscard.Old,
            Retention = StreamConfigRetention.Workqueue,
            AllowDirect = true,
        };

        return await js.CreateStreamAsync(config, lockCts.Token).ConfigureAwait(false);
    }

    protected virtual async Task<INatsJSConsumer> CreateConsumer(
        INatsJSStream jetStream,
        string consumerName,
        CancellationToken cancellationToken)
    {
        var config = new ConsumerConfig(consumerName) {
            MaxDeliver = Settings.MaxTryCount,
            FilterSubject = $"{GetPrefix()}commands.{GetRoleString(QueueId.HostRole)}.{QueueId.ShardIndex}.>",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromMinutes(15),
            MaxAckPending = 100,
            MaxBatch = 10,
            SampleFreq = "20%",
        };
        return await jetStream.CreateOrUpdateConsumerAsync(config, cancellationToken).ConfigureAwait(false);
    }

    protected string GetPrefix()
        => Settings.CommonPrefix == ""
            ? ""
            : $"{Settings.CommonPrefix}-";

    private enum CommandVersion : byte
    {
        Version1 = 1,
    }
}
