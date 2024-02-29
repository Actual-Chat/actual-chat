using System.Buffers;
using ActualChat.Mesh;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace ActualChat.Commands;

public class NatsEventQueue(QueueId queueId, NatsCommandQueues queues, IServiceProvider services) : NatsCommandQueue(queueId, queues, services), IEventQueueBackend
{
    private const string JetStreamName = "EVENTS";

    public async IAsyncEnumerable<QueuedCommand> Read(string consumerPrefix, Type commandType, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var consumerName = $"{consumerPrefix}-{QueueId.ShardIndex}";
        var jetStream = await EnsureStreamExists(cancellationToken).ConfigureAwait(false);
        var consumer = await EnsureConsumerExists(jetStream, consumerName, commandType, cancellationToken).ConfigureAwait(false);
        var messages = consumer.ConsumeAsync<IMemoryOwner<byte>>(
            opts: new NatsJSConsumeOpts {
                MaxMsgs = 10,
            },
            cancellationToken: cancellationToken);
        await foreach (var message in messages.ConfigureAwait(false)) {
            if (message.Data == null)
                continue;

            yield return DeserializeMessage(consumerPrefix, message);
        }
    }

    public async ValueTask MarkCompleted(string consumerPrefix, QueuedCommand command, CancellationToken cancellationToken)
    {
        var jetStream = await EnsureStreamExists(cancellationToken).ConfigureAwait(false);
        var perConsumerCommands = CommandsBeingProcessed.GetOrAdd(consumerPrefix, new ConcurrentDictionary<Ulid, NatsJSMsg<IMemoryOwner<byte>>>());
        if (!perConsumerCommands.TryRemove(command.Id, out var message)) {
            var streamName = jetStream.Info.Config.Name;
            Log.LogWarning("MarkCompleted. Event has already been completed. Id={Id}, StreamName={StreamName}, ConsumerPrefix={ConsumerPrefix}", command.Id, streamName, consumerPrefix);
            return;
        }

        await message.AckAsync(new AckOpts { DoubleAck = true }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask MarkFailed(
        string consumerPrefix,
        QueuedCommand command,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        var jetStream = await EnsureStreamExists(cancellationToken).ConfigureAwait(false);
        var perConsumerCommands = CommandsBeingProcessed.GetOrAdd(consumerPrefix, new ConcurrentDictionary<Ulid, NatsJSMsg<IMemoryOwner<byte>>>());
        if (!perConsumerCommands.TryRemove(command.Id, out var message)) {
            var streamName = jetStream.Info.Config.Name;
            Log.LogWarning("MarkFailed. Event has already been completed. Id={Id}, StreamName={StreamName}, ConsumerPrefix={ConsumerPrefix}", command.Id, streamName, consumerPrefix);
            return;
        }

        await message.NakAsync(new AckOpts { DoubleAck = true }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    protected override string BuildJetStreamName()
        => $"{GetPrefix()}{JetStreamName}";

    protected override string BuildSubject(Type commandType)
        => $"{GetPrefix()}events.{QueueId.ShardIndex}.{commandType.Name}";

    protected override async Task<INatsJSStream> CreateJetStream(NatsJSContext js, string jetStreamName, CancellationToken cancellationToken)
    {
        var meshLocks = Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(NatsEventQueue));
        var lockHolder = await meshLocks.Lock(nameof(EnsureStreamExists), "", cancellationToken).ConfigureAwait(false);
        await using var _ = lockHolder.ConfigureAwait(false);
        var lockCts = cancellationToken.LinkWith(lockHolder.StopToken);

        var config = new StreamConfig(jetStreamName, new[] { $"{GetPrefix()}events.>" }) {
            MaxMsgs = Queues.Settings.MaxQueueSize,
            Compression = StreamConfigCompression.S2,
            Storage = StreamConfigStorage.File,
            NumReplicas = Queues.Settings.ReplicaCount,
            Discard = StreamConfigDiscard.Old,
            Retention = StreamConfigRetention.Interest,
            AllowDirect = true,
        };

        var jsStream = await js.CreateStreamAsync(config, lockCts.Token).ConfigureAwait(false);
        return jsStream;
    }

    protected override async Task<INatsJSConsumer> CreateConsumer(
        INatsJSStream jetStream,
        string consumerName,
        Type commandType,
        CancellationToken cancellationToken)
    {
        var config = new ConsumerConfig(consumerName) {
            DurableName = consumerName,
            MaxDeliver = Settings.MaxTryCount,
            FilterSubject = $"{GetPrefix()}events.{QueueId.ShardIndex}.{commandType.Name}",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromMinutes(15),
            MaxAckPending = 100,
            MaxBatch = 10,
            SampleFreq = "20%",
            InactiveThreshold = TimeSpan.FromDays(1),
        };
        return await jetStream.CreateOrUpdateConsumerAsync(config, cancellationToken).ConfigureAwait(false);
    }
}
