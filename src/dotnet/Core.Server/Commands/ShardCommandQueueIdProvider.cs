using ActualChat.Hosting;

namespace ActualChat.Commands;

public class ShardCommandQueueIdProvider(IServiceProvider services) : ICommandQueueIdProvider
{
    private ILogger Log { get; } = services.LogFor<ShardCommandQueueIdProvider>();

    public QueueId Get(QueuedCommand command)
    {
        var type = command.UntypedCommand.GetType();
        var isEvent = typeof(IEventCommand).IsAssignableFrom(type);
        var shardKeyResolver = ShardKeyResolvers.GetUntyped(type) ?? ShardKeyResolvers.DefaultResolver;
        var shardKey = shardKeyResolver.Invoke(command.UntypedCommand);
        if (isEvent) {
            var eventShardIndex = ShardScheme.EventQueue.GetShardIndex(shardKey);
            return new QueueId(HostRole.EventQueue, eventShardIndex);
        }

        var hostRole = HostRoles.GetCommandRole(type);
        var scheme = ShardScheme.ById[hostRole.Id];
        var shardIndex = scheme.GetShardIndex(shardKey);
        return new QueueId(hostRole, shardIndex);
    }
}
