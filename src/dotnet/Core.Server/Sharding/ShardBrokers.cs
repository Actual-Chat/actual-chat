using ActualChat.Mesh;

namespace ActualChat;

public sealed class ShardBrokers : ProcessorBase, IHasServices
{
    private readonly ConcurrentDictionary<ShardScheme, LazySlim<ShardScheme, ShardBrokers, ShardBroker>> _brokers = new();

    internal IMeshLocks ShardLockRoot { get; }

    public IServiceProvider Services { get; }
    [field: AllowNull, MaybeNull]
    public BackendServiceDefs BackendServiceDefs => field ??= Services.BackendServiceDefs();
    [field: AllowNull, MaybeNull]
    public MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    [field: AllowNull, MaybeNull]
    public MeshNode ThisNode => field ??= MeshWatcher.ThisNode;
    [field: AllowNull, MaybeNull]
    public StateFactory StateFactory => field ??= Services.StateFactory();
    [field: AllowNull, MaybeNull]
    public MomentClock Clock { get; }

    public ShardBrokers(IServiceProvider services) : base(services.HostLifetimeIfExist().CreateStopTokenSource())
    {
        Services = services;
        ShardLockRoot = services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(ShardBrokers));
        Clock = ShardLockRoot.Clock;
    }

    public ShardBroker this[Type backendServiceType]
        => this[BackendServiceDefs[backendServiceType].ShardScheme];
    public ShardBroker this[ShardScheme shardScheme]
        => _brokers.GetOrAdd(
            shardScheme,
            static (shardScheme, self) => {
                var shardLocks = self.ShardLockRoot.WithKeyPrefix(shardScheme.Name);
                var stopTokenSource = self.StopToken.CreateLinkedTokenSource();
                return new ShardBroker(self, shardLocks, shardScheme, stopTokenSource).Start();
            },
            this);
}
