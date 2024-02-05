namespace ActualChat.Commands;

public class ShardCommandQueueIdProvider : ICommandQueueIdProvider
{
    public QueueId Get(QueuedCommand command)
    {
        var shardKeyResolver = ShardKeyResolvers.GetUntyped(command.UntypedCommand.GetType()) ?? ShardKeyResolvers.DefaultResolver;
        // TODO(AK): add host role support
        var shardKey = shardKeyResolver.Invoke(command.UntypedCommand);
        var shardIndex = ShardScheme.Backend.Instance.GetShardIndex(shardKey);
        return new QueueId(shardIndex, command.Priority);
    }
}
