using System.Text.RegularExpressions;
using ActualChat.Queues.Internal;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace ActualChat.Queues.Nats;

public sealed partial class NatsQueues(NatsQueues.Options settings, IServiceProvider services)
    : QueuesBase<NatsQueues.Options, NatsQueueProcessor>(settings, services)
{
    public sealed record Options : QueueSettings
    {
        public bool UseStreamPerShard { get; init; } = true;
        public int MaxQueueSize { get; init; } = 1024 * 1024;
        public int ReplicaCount { get; init; } = 0;
        public int MaxTryCount { get; set; } = 2;
    }

    [GeneratedRegex(@"[\.\[\]\<\>`']+")]
    private static partial Regex SpecialCharacterRegexFactory();
    private static readonly Regex SpecialCharacterRegex = SpecialCharacterRegexFactory();

    private readonly ConcurrentDictionary<(Type, Symbol), string> _getTopicCache = new();
    private NatsSettings? _natsSettings;
    private NatsConnection? _connection;

    private NatsConnection Connection {
        get {
            if (_connection != null)
                return _connection;

            lock (Lock)
                return _connection ??= Services.GetRequiredService<NatsConnection>();
        }
    }

    public NatsSettings NatsSettings => _natsSettings ??= Services.GetRequiredService<NatsSettings>();

    public override async Task Purge(CancellationToken cancellationToken = default)
    {
        var instancePrefix = NatsSettings.InstancePrefix;
        var context = new NatsJSContext(Connection);
        await foreach (var stream in context.ListStreamsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)) {
            if (!stream.Info.Config.Name.OrdinalStartsWith(instancePrefix))
                continue;

            await stream.PurgeAsync(new StreamPurgeRequest(), cancellationToken).ConfigureAwait(false);
        }
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

    protected override NatsQueueProcessor CreateProcessor(QueueRef queueRef)
        => new(Settings, this, queueRef);
}
