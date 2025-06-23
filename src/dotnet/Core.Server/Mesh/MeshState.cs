using ActualChat.Hosting;

namespace ActualChat.Mesh;

public sealed class MeshState
{
    private readonly Lock _lock = new();
    private volatile Dictionary<ShardScheme, ShardMap>? _shardMapCache;

    public ImmutableArray<MeshNode> OnlineNodes { get; }
    public ImmutableDictionary<NodeRef, CpuTimestamp> DyingNodes { get; }
    public ImmutableArray<MeshNode> Nodes { get; }
    public bool IsFinal { get; }

    // Computed properties
    public IReadOnlySet<HostRole> Roles { get; }
        = ImmutableHashSet<HostRole>.Empty;
    public IReadOnlyDictionary<NodeRef, MeshNode> NodeByRef { get; }
        = ImmutableDictionary<NodeRef, MeshNode>.Empty;
    public IReadOnlyDictionary<HostRole, ImmutableArray<MeshNode>> NodesByRole { get; }
        = ImmutableDictionary<HostRole, ImmutableArray<MeshNode>>.Empty;

    public MeshState(
        ImmutableArray<MeshNode> onlineNodes,
        ImmutableDictionary<NodeRef, CpuTimestamp> dyingNodes,
        ImmutableArray<MeshNode> nodes,
        bool isFinal = false)
    {
        OnlineNodes = onlineNodes;
        DyingNodes = dyingNodes;
        Nodes = nodes;
        IsFinal = isFinal;
        if (nodes.IsEmpty)
            return;

        Roles = Nodes.SelectMany(x => x.Roles).ToHashSet();
        NodeByRef = Nodes.ToDictionary(x => x.Ref, x => x);
        NodesByRole = Roles.Select(r => new KeyValuePair<HostRole, ImmutableArray<MeshNode>>(
            r,
            [..Nodes.Where(n => n.Roles.Contains(r))])
        ).ToDictionary();
    }

    public override string ToString()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append("MeshState(").Append(Nodes.Length).AppendLine(" node(s)) {");
        var now = CpuTimestamp.Now;
        var i = 0;
        foreach (var node in Nodes) {
            sb.Append("  [").Append(i).Append("] = ").Append(node);
            if (DyingNodes.TryGetValue(node.Ref, out var dyingAt) && dyingAt > now)
                sb.Append($", dying in {(now - dyingAt).ToShortString()}");
            sb.AppendLine();
            i++;
        }
        sb.Append('}');
        return sb.ToStringAndRelease();
    }

    public MeshState ToFinal()
        => IsFinal
            ? this
            : new MeshState(OnlineNodes, DyingNodes, Nodes, true);

    public (MeshNode? Node, MeshNodeState State) GetNodeAndState(NodeRef nodeRef)
    {
        if (nodeRef.IsNone)
            return (null, MeshNodeState.Dead);

        var node = NodeByRef.GetValueOrDefault(nodeRef);
        var state = DyingNodes.TryGetValue(nodeRef, out var dyingAt)
            ? dyingAt <= CpuTimestamp.Now
                ? MeshNodeState.Dead
                : MeshNodeState.Offline
            : node is not null
                ? MeshNodeState.Online
                : MeshNodeState.Unknown;
        return (node, state);
    }

    public ShardMap GetShardMap(ShardScheme shardScheme)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var cache = _shardMapCache;
        if (cache != null && cache.TryGetValue(shardScheme, out var shardMap))
            return shardMap;

        lock (_lock) { // Double-check locking
            cache = _shardMapCache;
            if (cache != null && cache.TryGetValue(shardScheme, out shardMap))
                return shardMap;

            if (!NodesByRole.TryGetValue(shardScheme.HostRole, out var nodes))
                nodes = ImmutableArray<MeshNode>.Empty;
            shardMap = new ShardMap(shardScheme, nodes);
            cache = cache == null ? new() : new Dictionary<ShardScheme, ShardMap>(cache);
            cache[shardScheme] = shardMap;
            _shardMapCache = cache;
        }
        return shardMap;
    }
}
