namespace ActualChat.Commands;

public class ShardCommandQueueIdProvider : ICommandQueueIdProvider
{
    public QueueId Get(QueuedCommand command)
    {
        var meshRefResolver = ValueMeshRefResolvers.GetUntyped(command.UntypedCommand.GetType()) ?? ValueMeshRefResolvers.RandomShard<ICommand>();
        // TODO(AK): add host role support
        var meshRef = meshRefResolver.Invoke(command.UntypedCommand, ShardScheme.Backend.Instance);
        var shardIndex = meshRef.Kind == MeshRefKind.NodeRef
            ? meshRef.ShardRef.ShardKey
            : 0;
        return new QueueId(shardIndex, command.Priority);
    }
}
