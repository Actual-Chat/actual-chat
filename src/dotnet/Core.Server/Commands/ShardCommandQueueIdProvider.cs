using ActualChat.Hosting;

namespace ActualChat.Commands;

public class ShardCommandQueueIdProvider(IServiceProvider services) : ICommandQueueIdProvider
{
    private ILogger Log { get; } = services.LogFor<ShardCommandQueueIdProvider>();

    public QueueId Get(QueuedCommand command)
    {
        var type = command.UntypedCommand.GetType();
        var isEvent = typeof(IEventCommand).IsAssignableFrom(type);
        var hostRoles = HostRoles.GetServedByRoles(type)
            .Where(hr => hr.IsBackendServer)
            .ToList();
        var shardKeyResolver = ShardKeyResolvers.GetUntyped(type) ?? ShardKeyResolvers.DefaultResolver;
        var shardKey = shardKeyResolver.Invoke(command.UntypedCommand);
        if (isEvent) {
            var shardIndex = ShardScheme.EventQueue.Instance.GetShardIndex(shardKey);
            return new QueueId(HostRole.EventQueue, shardIndex);
        }
        if (hostRoles.Count == 0)
            throw StandardError.Configuration($"There are no host roles found for a {type}.");

        if (hostRoles.Count > 1) {
            Log.LogWarning("There are {HostRoleCount} host roles found for a {CommandType}. Handling the first one...", hostRoles.Count, type);
            var hostRole = hostRoles[0];
            var shardScheme = ShardScheme.ById[hostRole.Id];
            var shardIndex = shardScheme.GetShardIndex(shardKey);
            return new QueueId(hostRole, shardIndex);
        }
        else {
            var hostRole = hostRoles[0];
            var scheme = ShardScheme.ById[hostRole.Id];
            var shardIndex = scheme.GetShardIndex(shardKey);
            return new QueueId(hostRole, shardIndex);
        }
    }
}
