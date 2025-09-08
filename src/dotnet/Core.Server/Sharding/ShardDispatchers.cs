using ActualChat.Mesh;

namespace ActualChat;

public sealed class ShardDispatchers(IServiceProvider services)
    : ProcessorBase(services.HostLifetimeIfExist().CreateStopTokenSource()), IHasServices
{
    private readonly ConcurrentDictionary<
        (ShardScheme ShardScheme, string KeyPrefix),
        LazySlim<(ShardScheme ShardScheme, string KeyPrefix), ShardDispatchers, ShardDispatcher>> _dispatchers
        = new();

    private IMeshLocks ShardLocks { get; } = services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(ShardDispatchers));

    public IServiceProvider Services { get; } = services;
    [field: AllowNull, MaybeNull]
    public BackendServiceDefs BackendServiceDefs => field ??= Services.BackendServiceDefs();
    [field: AllowNull, MaybeNull]
    public MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    [field: AllowNull, MaybeNull]
    public MeshNode ThisNode => field ??= MeshWatcher.ThisNode;
    [field: AllowNull, MaybeNull]
    public StateFactory StateFactory => field ??= Services.StateFactory();

    // Internal properties

    public ShardDispatcher this[Type serviceType, string? keyPrefix = null]
        => this[BackendServiceDefs[serviceType].ShardScheme, keyPrefix];
    public ShardDispatcher this[ShardScheme shardScheme, string? keyPrefix = null] {
        get {
            keyPrefix ??= "";
            return _dispatchers.GetOrAdd(
                (shardScheme, keyPrefix),
                static (key, self) => {
                    var (shardScheme, keyPrefix) = key;
                    var shardLocks = self.GetShardLocks(shardScheme, keyPrefix);
                    var stopTokenSource = self.StopToken.CreateLinkedTokenSource();
                    var shardLocker = new ShardDispatcher(self, shardLocks, shardScheme, keyPrefix, stopTokenSource);
                    shardLocker.Start();
                    return shardLocker;
                },
                this);
        }
    }

    public static string ComposeFullKeyPrefix(ShardScheme shardScheme, string keyPrefix)
        => keyPrefix.IsNullOrEmpty()
            ? shardScheme.RequireValid().Id.Value
            : $"{keyPrefix}.{shardScheme.RequireValid().Id.Value}";

    public IMeshLocks GetShardLocks(ShardScheme shardScheme, string keyPrefix)
        => ShardLocks.WithKeyPrefix(ComposeFullKeyPrefix(shardScheme, keyPrefix));
}
