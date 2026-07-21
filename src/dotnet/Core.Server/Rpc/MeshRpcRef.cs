using ActualLab.Rpc;
namespace ActualChat.Rpc;

/// <summary>
/// A stable <see cref="RpcRef"/> per <see cref="MeshRef"/>: shard refs reroute via
/// per-generation <see cref="MeshRpcRoute"/>-s, node refs use a static route.
/// </summary>
public sealed class MeshRpcRef : RpcRef
{
    public readonly MeshRpcRefs Owner;
    public readonly MeshRef MeshRef;
    public readonly IState<ShardOwner.ShardState>? ShardState;

    // Computed properties
    public ShardRef ShardRef => MeshRef.ShardRef;
    public ResolvedMeshRef Resolved => Route is MeshRpcRoute meshRoute
        ? meshRoute.Resolved
        : new ResolvedMeshRef(Owner, MeshRef);
    public NodeRef NodeRef => Resolved.NodeRef;
    public MeshNode? Node => Resolved.Node;

    internal MeshRpcRef(MeshRpcRefs owner, MeshRef meshRef)
    {
        Owner = owner;
        MeshRef = meshRef;
        IsBackend = true;
        HostInfo = meshRef.ToString();
        UseReferentialEquality = true;

        var shardRef = meshRef.ShardRef;
        if (shardRef.IsNone) {
            // Node refs never reroute, so their connection kind is resolved just once
            ConnectionKind = new ResolvedMeshRef(owner, meshRef).ConnectionKind;
        }
        else {
            var shardIndex = shardRef.GetShardIndex();
            var shardOwner = owner.ShardOwners[shardRef.Scheme];
            ShardState = shardOwner.States[shardIndex];
        }
        Initialize();
    }

    // Protected methods

    protected override RpcRoute CreateRoute()
        => ShardState is null
            ? base.CreateRoute() // Static route: node refs never reroute
            : new MeshRpcRoute(this);
}
