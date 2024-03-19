using System.Text.RegularExpressions;
using ActualChat.Hosting;
using ActualChat.Queues.Internal;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace ActualChat.Queues.Nats;

public sealed partial class NatsQueues(NatsQueues.Options settings, IServiceProvider services) : WorkerBase, IQueues
{
    [GeneratedRegex(@"[\.\[\]\<\>`']+")]
    private static partial Regex SpecialCharacterRegexFactory();
    private static readonly Regex SpecialCharacterRegex = SpecialCharacterRegexFactory();

    private readonly ConcurrentDictionary<(Type, Symbol), string> _getTopicCache = new();
    private readonly ConcurrentDictionary<QueueRef, NatsQueueProcessor> _queues = new ();

    public sealed record Options : IShardQueueSettings
    {
        public string InstanceName { get; init; } = "";
        public bool UseStreamPerShard { get; init; } = true;
        public int ConcurrencyLevel { get; init; } = HardwareInfo.GetProcessorCountFactor(8);
        public int MaxQueueSize { get; init; } = 1024 * 1024;
        public int ReplicaCount { get; init; } = 0;
        public int MaxTryCount { get; set; } = 2;
        public IMomentClock? Clock { get; init; }
    }

    private readonly object _lock = new();
    private NatsConnection? _nats;

    private NatsConnection Nats {
        get {
            if (_nats != null)
                return _nats;

            lock (_lock)
                return _nats = Services.GetRequiredService<NatsConnection>();
        }
    }

    public IServiceProvider Services { get; } = services;
    public HostInfo HostInfo { get; } = services.HostInfo();
    public IMomentClock Clock { get; } = settings.Clock ?? services.Clocks().SystemClock;
    public Options Settings { get; } = settings;

    public IQueueProcessor GetProcessor(QueueRef queueRef)
        => _queues.GetOrAdd(queueRef,
            static (queueRef1, self) => new NatsQueueProcessor(self.Settings, self, queueRef1),
            this);

    public async Task Purge(CancellationToken cancellationToken)
    {
        var context = new NatsJSContext(Nats);
        await foreach (var stream in context.ListStreamsAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            await stream.PurgeAsync(new StreamPurgeRequest(), cancellationToken).ConfigureAwait(false);
    }

    public string GetTopic(ICommand command)
    {
        var eventCommand = command as IEventCommand;
        return _getTopicCache.GetOrAdd((command.GetType(), eventCommand?.ChainId ?? default), key => {
            var (commandType, chainId) = key;
            var sCommandType = commandType.ToIdentifierName();
            if (chainId.IsEmpty)
                return sCommandType;

            var sChainId = chainId.Value;
            if (sChainId.OrdinalHasPrefix("ActualChat.", out var suffix))
                sChainId = suffix;
            if (sChainId.OrdinalHasPrefix("ActualLab.", out suffix))
                sChainId = suffix;
            sChainId = SpecialCharacterRegex.Replace(sChainId, "-");
            return $"{sCommandType}-{sChainId}";
        });
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var hostedQueueRefs = GetHostedQueues();
        foreach (var queueRef in hostedQueueRefs)
            GetProcessor(queueRef).Start();
        try {
            await ActualLab.Async.TaskExt.NeverEndingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            await hostedQueueRefs
                .Select(x => GetProcessor(x).DisposeSilentlyAsync().AsTask())
                .Collect()
                .ConfigureAwait(false);
        }
    }

    private HashSet<QueueRef> GetHostedQueues()
    {
        var result = new HashSet<QueueRef>();
        foreach (var shardScheme in ShardScheme.ById.Values) {
            if (!(shardScheme.IsValid && HostInfo.HasRole(shardScheme.HostRole)))
                continue;

            result.Add(shardScheme);
        }
        return result;
    }
}
