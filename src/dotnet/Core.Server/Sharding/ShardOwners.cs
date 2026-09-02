namespace ActualChat.Sharding;

public sealed class ShardOwners : ProcessorBase, IHasServices
{
    private readonly ConcurrentDictionary<ShardScheme, LazySlim<ShardScheme, ShardOwners, ShardOwner>> _owners = new();

    // OwnershipLocks root, see how it's used
    internal IMeshLocks OwnershipLocks { get; }

    public IServiceProvider Services { get; }
    public BackendServiceDefs BackendServiceDefs => field ??= Services.BackendServiceDefs();
    public MeshWatcher MeshWatcher => field ??= Services.MeshWatcher();
    public MeshNode ThisNode => field ??= MeshWatcher.ThisNode;
    public StateFactory StateFactory => field ??= Services.StateFactory();
    public MomentClock Clock { get; }

    public ShardOwners(IServiceProvider services) : base(services.HostLifetimeIfExist().CreateStopTokenSource())
    {
        Services = services;
        OwnershipLocks = services.MeshLocks().WithKeyPrefix(nameof(ShardOwners));
        Clock = OwnershipLocks.Clock;
    }

    public Task Stop()
    {
        StopTokenSource.CancelAndDisposeSilently();
        return Task.WhenAll(_owners.Values.Where(x => x.HasValue).Select(x => x.Value.Stop()));
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
