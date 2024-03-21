using ActualChat.Commands;
using ActualChat.Hosting;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace ActualChat.Nats;

public class NatsCommandQueues(NatsCommandQueues.Options settings, IServiceProvider services) : ICommandQueues
{
    private readonly ConcurrentDictionary<QueueId, NatsCommandQueue> _queues = new ();

    public sealed record Options
    {
        public string CommonPrefix { get; init; } = "";
        public int MaxQueueSize { get; init; } = Constants.Queues.SharedCommandQueueDefaultSize;
        public int ReplicaCount { get; init; } = 0;
        public int MaxTryCount { get; set; } = 2;
    }

    private readonly object _lock = new ();
    private NatsConnection? _nats;
    protected NatsConnection Nats => _nats ??= GetConnection();

    public IServiceProvider Services { get; } = services;
    public IMomentClock Clock { get; } = services.Clocks().SystemClock;
    public Options Settings { get; } = settings;

    public ICommandQueue this[QueueId queueId] => Get(queueId);

    public ICommandQueueBackend GetBackend(QueueId queueId)
        => Get(queueId);

    public async Task Purge(CancellationToken cancellationToken)
    {
        var js = new NatsJSContext(Nats);
        await foreach (var jsStream in js.ListStreamsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            await jsStream.PurgeAsync(new StreamPurgeRequest(), cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private NatsConnection GetConnection()
    {
        lock (_lock)
            return _nats = Services.GetRequiredService<NatsConnection>();
    }


    private NatsCommandQueue Get(QueueId queueId)
        => _queues.GetOrAdd(
            queueId,
            static (queueId1, self) => queueId1.HostRole == HostRole.EventQueue
                ? new NatsEventQueue(queueId1, self, self.Services)
                : new NatsCommandQueue(queueId1, self, self.Services),
            this);
}
