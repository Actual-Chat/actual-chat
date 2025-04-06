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
        public int MaxTryCount { get; set; } = 10;
    }

    [GeneratedRegex(@"[\.\[\]\<\>`']+")]
    private static partial Regex SpecialCharacterRegexFactory();
    private static readonly Regex SpecialCharacterRegex = SpecialCharacterRegexFactory();

    private readonly ConcurrentDictionary<(Type, string), string> _getTopicCache = new();

    [field: AllowNull, MaybeNull]
    private NatsConnection Connection {
        get {
            if (field != null)
                return field;

            lock (Lock)
                return field ??= Services.GetRequiredService<NatsConnection>();
        }
    }

    [field: AllowNull, MaybeNull]
    public NatsSettings NatsSettings => field ??= Services.GetRequiredService<NatsSettings>();

    public override async Task Purge(CancellationToken cancellationToken = default)
    {
        var instancePrefix = NatsSettings.InstancePrefix;
        var context = new NatsJSContext(Connection);
        await foreach (var stream in context.ListStreamsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)) {
            if (!stream.Info.Config.Name.OrdinalStartsWith(instancePrefix))
                continue;

            // await stream.DeleteAsync(cancellationToken).ConfigureAwait(false);
            await stream.PurgeAsync(new StreamPurgeRequest(), cancellationToken).ConfigureAwait(false);
        }
    }

    public string GetTopic(ICommand command)
    {
        var eventCommand = command as IEventCommand;
        return _getTopicCache.GetOrAdd((command.GetType(), eventCommand?.ChainId ?? ""), key => {
            var (commandType, chainId) = key;
            var sCommandType = commandType.ToIdentifierName();
            if (chainId.IsNullOrEmpty())
                return sCommandType;

            if (chainId.OrdinalHasPrefix("ActualChat.", out var suffix))
                chainId = suffix;
            if (chainId.OrdinalHasPrefix("ActualLab.", out suffix))
                chainId = suffix;
            chainId = SpecialCharacterRegex.Replace(chainId, "-");
            return $"{sCommandType}-{chainId}";
        });
    }

    // Protected methods

    protected override NatsQueueProcessor CreateProcessor(QueueRef queueRef)
        => new(Settings, this, queueRef);
}
