using ActualChat.Hosting;

namespace ActualChat.Commands;

public class ShardCommandQueueIdProvider : ICommandQueueIdProvider
{
    public QueueId Get(QueuedCommand command)
    {
        var type = command.UntypedCommand.GetType();
        var servedByRoles = HostRoles.GetServedByRoles(type);
        var shardKeyResolver = ShardKeyResolvers.GetUntyped(type) ?? ShardKeyResolvers.DefaultResolver;
        // TODO(AK): add host role support
        var shardKey = shardKeyResolver.Invoke(command.UntypedCommand);
        var shardIndex = ShardScheme.Backend.Instance.GetShardIndex(shardKey);
        return new QueueId(shardIndex, command.Priority);
    }
}
