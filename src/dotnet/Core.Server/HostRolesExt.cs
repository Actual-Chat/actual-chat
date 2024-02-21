using ActualChat.Attributes;
using ActualChat.Hosting;

namespace ActualChat;

public static class HostRolesExt
{
    private static readonly ConcurrentDictionary<object, ServiceModeAttribute[]> _serviceModeAttributes = new();
    private static readonly ConcurrentDictionary<object, ShardSchemeAttribute?> _shardSchemeAttributes = new();
    private static readonly ConcurrentDictionary<object, CommandQueueAttribute?> _commandQueueAttributes = new();

    // GetServiceMode

    public static ServiceMode GetServiceMode<T>(this IReadOnlySet<HostRole> hostRoles)
        => hostRoles.GetServiceMode(typeof(T));
    public static ServiceMode GetServiceMode(this IReadOnlySet<HostRole> hostRoles, Type type)
    {
        var attrs = _serviceModeAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<ServiceModeAttribute>()
                .OrderByDescending(x => x.Priority)
                .ToArray(),
            type);
        if (attrs.Length == 0)
            return hostRoles.GetServiceMode(type.Assembly);

        var attr = attrs.FirstOrDefault(x => hostRoles.Contains(new HostRole(x.HostRole)));
        return attr?.ServiceMode ?? ServiceMode.Client;
    }

    public static ServiceMode GetServiceMode(this IReadOnlySet<HostRole> hostRoles, Assembly assembly)
    {
        var attrs = _serviceModeAttributes.GetOrAdd(assembly,
            static (_, a) => a
                .GetCustomAttributes<ServiceModeAttribute>()
                .OrderByDescending(x => x.Priority)
                .ToArray(),
            assembly);
        var attr = attrs.FirstOrDefault(x => hostRoles.Contains(new HostRole(x.HostRole)));
        return attr?.ServiceMode ?? ServiceMode.Client;
    }

    // GetShardScheme

    public static ShardScheme GetShardScheme<T>(this IReadOnlySet<HostRole> hostRoles)
        => hostRoles.GetShardScheme(typeof(T));
    public static ShardScheme GetShardScheme(this IReadOnlySet<HostRole> hostRoles, Type type)
    {
        var attr = _shardSchemeAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<ShardSchemeAttribute>()
                .SingleOrDefault(),
            type);
        return attr == null
            ? hostRoles.GetShardScheme(type.Assembly)
            : ShardScheme.ById[attr.ShardScheme];
    }

    public static ShardScheme GetShardScheme(this IReadOnlySet<HostRole> hostRoles, Assembly assembly)
    {
        var attr = _shardSchemeAttributes.GetOrAdd(assembly,
            static (_, t) => t
                .GetCustomAttributes<ShardSchemeAttribute>()
                .SingleOrDefault(),
            assembly);
        return attr == null
            ? ShardScheme.None.Instance
            : ShardScheme.ById[attr.ShardScheme];
    }

    // GetCommandQueue

    public static ShardScheme GetCommandQueue<T>(this IReadOnlySet<HostRole> hostRoles)
        => hostRoles.GetCommandQueue(typeof(T));
    public static ShardScheme GetCommandQueue(this IReadOnlySet<HostRole> hostRoles, Type type)
    {
        var attr = _commandQueueAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<CommandQueueAttribute>()
                .SingleOrDefault(),
            type);
        if (attr == null)
            return hostRoles.GetCommandQueue(type.Assembly);

        var shardScheme = ShardScheme.ById[attr.QueueShardScheme];
        if (!shardScheme.IsQueue)
            throw StandardError.Internal($"Type '{type.FullName}' has invalid {nameof(CommandQueueAttribute)}.");

        return shardScheme;
    }

    public static ShardScheme GetCommandQueue(this IReadOnlySet<HostRole> hostRoles, Assembly assembly)
    {
        var attr = _commandQueueAttributes.GetOrAdd(assembly,
            static (_, t) => t
                .GetCustomAttributes<CommandQueueAttribute>()
                .SingleOrDefault(),
            assembly);
        if (attr == null)
            return ShardScheme.None.Instance;

        var shardScheme = ShardScheme.ById[attr.QueueShardScheme];
        if (!shardScheme.IsQueue)
            throw StandardError.Internal($"Assembly '{assembly.FullName}' has invalid {nameof(CommandQueueAttribute)}.");

        return shardScheme;
    }
}
