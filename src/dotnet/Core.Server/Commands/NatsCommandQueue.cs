using System.Buffers;
using ActualChat.Mesh;
using ActualLab.IO;
using ActualLab.Locking;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace ActualChat.Commands;

public class NatsCommandQueue(QueueId queueId, NatsCommandQueues queues, IServiceProvider services) : WorkerBase, ICommandQueue, ICommandQueueBackend
{
    private const string CommandStreamName = "COMMANDS";
    private static readonly byte[] Version = [ (byte)CommandVersion.Version1 ];
    private static readonly HashSet<byte> SupportedVersions = [
        (byte)CommandVersion.Version1,
    ];

    private volatile ConcurrentDictionary<Symbol, NatsJSMsg<IMemoryOwner<byte>>> _commandsBeingProcessed = new ();
    private volatile Channel<QueuedCommand> _readChannel = CreateReadChannel();

    private static Channel<QueuedCommand> CreateReadChannel()
        => Channel.CreateBounded<QueuedCommand>(
            new BoundedChannelOptions(Constants.Queues.LocalCommandQueueDefaultSize) {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

    private NatsConnection? _nats;
    private volatile INatsJSStream? _jetStream;
    private volatile INatsJSConsumer? _jetStreamConsumer;

    private NatsConnection Nats => _nats ??= GetConnection();
    private AsyncLock AsyncLock { get; } = new(LockReentryMode.CheckedPass);
    private ILogger<NatsCommandQueue> Log { get; } = services.LogFor<NatsCommandQueue>();

    public QueueId QueueId { get; } = queueId;
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
            command.Ulid.TryWriteBytes(bufferWriter.GetSpan(16));
            bufferWriter.Advance(16);

            typedSerializer.Write(bufferWriter, command.UntypedCommand, commandType);
            var subject = $"commands.any.{QueueId.ShardIndex}.{commandType.Name}";
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

    public IAsyncEnumerable<QueuedCommand> Read(CancellationToken cancellationToken)
    {
        _ = Run();
        return _readChannel.Reader.ReadAllAsync(cancellationToken);
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

    public async ValueTask MarkFailed(QueuedCommand command, bool mustRetry, Exception? exception, CancellationToken cancellationToken)
    {
        if (!_commandsBeingProcessed.TryRemove(command.Id, out var message)) {
            var streamName = _jetStream?.Info.Config.Name;
            Log.LogWarning("MarkFailed. Command has already been completed. Id={Id}, StreamName={StreamName}", command.Id, streamName);
            return;
        }

        if (!mustRetry) {
            await message.AckTerminateAsync(new AckOpts { DoubleAck = true }, cancellationToken).ConfigureAwait(false);
            return;
        }

        // TODO(AK): We do not use NATS retries - there is no Redelivery counter - we re-add command for reprocessing locally
        var newCommand = command.WithRetry();
        await _readChannel.Writer.WriteAsync(newCommand, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(ReadQueue),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        await (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            )
            .RunIsolated(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ReadQueue(CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            var typedSerializer = TypeDecoratingByteSerializer.Default;
            var consumer = await EnsureConsumerExists(cancellationToken).ConfigureAwait(false);
            var messages = consumer.ConsumeAsync<IMemoryOwner<byte>>(
                opts: new NatsJSConsumeOpts {
                    MaxMsgs = 10,
                },
                cancellationToken: cancellationToken);
            await foreach (var message in messages) {
                if (message.Data == null)
                    continue;

                var version = message.Data.Memory.Span[0];
                if (!SupportedVersions.Contains(version))
                    throw StandardError.NotSupported($"CommandVersion={version} is not supported.");

                var id = new Ulid(message.Data.Memory.Span[1..17]);
                var command = typedSerializer.Read<ICommand>(message.Data.Memory[17..]);
                var queuedCommand = QueuedCommand.FromCommand(id, command);
                if (_commandsBeingProcessed.TryAdd(queuedCommand.Id, message)) // double check duplicates - we have already handled them at the NATS stream
                    await _readChannel.Writer.WriteAsync(queuedCommand, cancellationToken).ConfigureAwait(false);

                message.Data.DisposeSilently(); // release buffer
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            error = e;
        }
        finally {
            _readChannel.Writer.TryComplete(error);
            _readChannel = CreateReadChannel();
        }
    }

    private NatsConnection GetConnection()
    {
        lock (this)
            return _nats = Services.GetRequiredService<NatsConnection>();
    }

    private async ValueTask<INatsJSStream> EnsureStreamExists(CancellationToken cancellationToken)
    {
        if (_jetStream != null)
            return _jetStream;

        using var releaser = await AsyncLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        if (_jetStream != null)
            return _jetStream;

        var js = new NatsJSContext(Nats);
        var retryCount = 0;
        while (_jetStream == null)
            try {
                var jetStream = await js
                    .GetStreamAsync(CommandStreamName, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _jetStream = jetStream;

            }
            catch (NatsJSApiException e) when (e.Error.Code == 404) {
                _jetStream = await CreateCommandsStream(cancellationToken);
            }
            catch (NatsJSApiNoResponseException e) {
                if (retryCount++ > 3)
                    throw;

                Log.LogWarning(e, "EnsureStreamExists: error getting stream");
                var delay = Random.Shared.Next(100, 250);
                await Services.Clocks().SystemClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

        return _jetStream;

        async Task<INatsJSStream> CreateCommandsStream(CancellationToken cancellationToken1)
        {
            var meshLocks = Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(NatsCommandQueue));
            var lockHolder = await meshLocks.Lock(nameof(EnsureStreamExists), "", cancellationToken1).ConfigureAwait(false);
            await using var _ = lockHolder.ConfigureAwait(false);
            var lockCts = cancellationToken1.LinkWith(lockHolder.StopToken);

            var config = new StreamConfig(CommandStreamName, new[] { "commands.>" }) {
                MaxMsgs = queues.Settings.MaxQueueSize,
                Compression = StreamConfigCompression.S2,
                Storage = StreamConfigStorage.File,
                NumReplicas = queues.Settings.ReplicaCount,
                Discard = StreamConfigDiscard.Old,
                Retention = StreamConfigRetention.Workqueue,
                AllowDirect = true,
            };

            return await js.CreateStreamAsync(config, lockCts.Token).ConfigureAwait(false);
        }

    }

    private async ValueTask<INatsJSConsumer> EnsureConsumerExists(CancellationToken cancellationToken)
    {
        if (_jetStreamConsumer != null)
            return _jetStreamConsumer!;

        using var releaser = await AsyncLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        if (_jetStreamConsumer != null)
            return _jetStreamConsumer!;

        var stream = await EnsureStreamExists(cancellationToken).ConfigureAwait(false);
        var name = $"WORKER-{QueueId.ShardIndex}";
        var config = new ConsumerConfig(name) {
            MaxDeliver = Settings.MaxTryCount,
            FilterSubject = $"commands.*.{QueueId.ShardIndex}.>",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromMinutes(15),
            MaxAckPending = 100,
            MaxBatch = 10,
            SampleFreq = "20%",
        };
        return _jetStreamConsumer = await stream.CreateOrUpdateConsumerAsync(config, cancellationToken).ConfigureAwait(false);
    }

    private enum CommandVersion : byte
    {
        Version1 = 1,
    }
}
