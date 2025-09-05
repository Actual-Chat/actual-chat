using ActualChat.Mesh;
using Microsoft.Extensions.Hosting;

namespace ActualChat;

public sealed class ShardLockers(IServiceProvider services) : IHasServices
{
    private readonly ConcurrentDictionary<
        (ShardScheme ShardScheme, string KeyPrefix),
        LazySlim<(ShardScheme ShardScheme, string KeyPrefix), ShardLockers, ShardLocker>>
        _shardLockers = new();

    internal StateFactory StateFactory { get; } = services.StateFactory();
    internal IHostApplicationLifetime? HostApplicationLifetime { get; } = services.HostLifetimeIfExist();
    internal IMeshLocks ShardLocks { get; } = services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(ShardLockers));

    public IServiceProvider Services { get; } = services;
    [field: AllowNull, MaybeNull]
    public BackendServiceDefs BackendServiceDefs => field ??= Services.BackendServiceDefs();
    [field: AllowNull, MaybeNull]
    public MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    [field: AllowNull, MaybeNull]
    public MeshNode ThisNode => field ??= MeshWatcher.ThisNode;

    // Internal properties

    public ShardLocker this[Type serviceType, string? keyPrefix = null]
        => this[BackendServiceDefs[serviceType].ShardScheme, keyPrefix];
    public ShardLocker this[ShardScheme shardScheme, string? keyPrefix = null] {
        get {
            keyPrefix = keyPrefix.NullIfEmpty() ?? "Default";
            return _shardLockers.GetOrAdd(
                (shardScheme, keyPrefix),
                static (key, self) => {
                    var (shardScheme, keyPrefix) = key;
                    var shardLocks = self.GetShardLocks(shardScheme, keyPrefix);
                    var stopTokenSource = self.HostApplicationLifetime?.ApplicationStopping.CreateLinkedTokenSource();
                    var shardLocker = new ShardLocker(self, shardLocks, shardScheme, keyPrefix, stopTokenSource);
                    shardLocker.Start();
                    return shardLocker;
                },
                this);
        }
    }

    public IMeshLocks GetShardLocks(ShardScheme shardScheme, string keyPrefix)
        => ShardLocks.WithKeyPrefix($"{keyPrefix}.{shardScheme.RequireValid().Id.Value}");
}
