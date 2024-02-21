using System.Buffers;
using ActualChat.Hosting;
using ActualChat.Mesh;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace ActualChat.Commands;

public class NatsEventQueue(QueueId queueId, NatsCommandQueues queues, IServiceProvider services) : NatsCommandQueue(queueId, queues, services), IEventQueueBackend
{
    private const string JetStreamName = "EVENTS";

    private readonly ConcurrentDictionary<Symbol, INatsJSConsumer> _consumers = new ();

    public override IAsyncEnumerable<QueuedCommand> Read(CancellationToken cancellationToken)
        => throw StandardError.NotSupported("Reading events stream without specifying backend HostRole is not supported.");

    public async IAsyncEnumerable<QueuedCommand> Read(HostRole hostRole, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var backendRole = hostRole.RequireBackend();
        var jetStream = await EnsureStreamExists(cancellationToken).ConfigureAwait(false);
        var consumer = await EnsureConsumerExists(jetStream, backendRole, cancellationToken).ConfigureAwait(false);
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

    protected override Task<INatsJSConsumer> CreateConsumer(INatsJSStream jetStream, string consumerName, CancellationToken cancellationToken)
        => CreateConsumer(jetStream, HostRole.EventQueue, cancellationToken);

    private async ValueTask<INatsJSConsumer> EnsureConsumerExists(INatsJSStream jetStream, HostRole hostRole, CancellationToken cancellationToken)
    {
        if (_consumers.TryGetValue(hostRole.Id, out var consumer))
            return consumer;

        using var releaser = await AsyncLock.Lock(cancellationToken).ConfigureAwait(false);
        releaser.MarkLockedLocally();

        if (_consumers.TryGetValue(hostRole.Id, out consumer))
            return consumer;

        var consumerName = BuildConsumerName(hostRole);
        var retryCount = 0;
        while (consumer == null)
            try {
                consumer = await jetStream.GetConsumerAsync(consumerName, cancellationToken).ConfigureAwait(false);
            }
            catch (NatsJSApiException e) when (e.Error.Code == 404) {
                consumer = await CreateConsumer(jetStream, hostRole, cancellationToken).ConfigureAwait(false);
            }
            catch (NatsJSApiNoResponseException e) {
                if (retryCount++ > 3)
                    throw;

                Log.LogWarning(e, "EnsureStreamExists: error getting stream");
                var delay = Random.Shared.Next(100, 250);
                await Services.Clocks().SystemClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

        return _consumers.GetOrAdd(hostRole.Id, consumer);
    }

    protected override string BuildConsumerName(HostRole hostRole)
    {
        var backend = GetRoleString(hostRole);
        var name = $"LISTENER-{QueueId.ShardIndex}-{backend}";
        return name;
    }

    private async Task<INatsJSConsumer> CreateConsumer(INatsJSStream jetStream, HostRole hostRole, CancellationToken cancellationToken)
    {
        var name = BuildConsumerName(hostRole);
        var config = new ConsumerConfig(name) {
            DurableName = name,
            MaxDeliver = Settings.MaxTryCount,
            FilterSubject = $"{GetPrefix()}events.{QueueId.ShardIndex}.>",
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromMinutes(15),
            MaxAckPending = 100,
            MaxBatch = 10,
            SampleFreq = "20%",
        };
        return await jetStream.CreateOrUpdateConsumerAsync(config, cancellationToken).ConfigureAwait(false);
    }
}
