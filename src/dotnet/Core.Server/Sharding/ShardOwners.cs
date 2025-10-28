namespace ActualChat.Sharding;

public sealed class ShardOwners : ProcessorBase, IHasServices
{
    private readonly ConcurrentDictionary<ShardScheme, LazySlim<ShardScheme, ShardOwners, ShardOwner>> _owners = new();

    // OwnershipLocks root, see how it's used
    internal IMeshLocks OwnershipLocks { get; }

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

    public ShardOwners(IServiceProvider services) : base(services.HostLifetimeIfExist().CreateStopTokenSource())
    {
        Services = services;
        OwnershipLocks = services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(ShardOwners));
        Clock = OwnershipLocks.Clock;
    }

    public ShardOwner this[Type backendServiceType]
        => this[BackendServiceDefs[backendServiceType].ShardScheme];
    public ShardOwner this[ShardScheme shardScheme]
        => _owners.GetOrAdd(
            shardScheme,
            static (shardScheme, self) => {
                var ownershipLocks = self.OwnershipLocks.WithKeyPrefix(shardScheme.Name);
                var stopTokenSource = self.StopToken.CreateLinkedTokenSource();
                return new ShardOwner(self, ownershipLocks, shardScheme, stopTokenSource).Start();
            },
            this);
}
