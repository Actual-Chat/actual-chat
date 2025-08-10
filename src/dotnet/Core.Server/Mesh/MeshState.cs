using ActualChat.Hosting;

namespace ActualChat.Mesh;

public sealed class MeshState
{
    private readonly Lock _lock = new();
    private volatile Dictionary<ShardScheme, ShardMap>? _shardMapCache;

    public MomentClock Clock { get; set; }
    public ImmutableDictionary<NodeRef, MeshNode> AllNodes { get; }
    public bool IsFinal { get; }

    // Computed properties
    public IReadOnlySet<HostRole> AllRoles { get; } = ImmutableHashSet<HostRole>.Empty;
    public ImmutableArray<MeshNode> LiveNodes { get; } = ImmutableArray<MeshNode>.Empty;
    public IReadOnlyDictionary<HostRole, ImmutableArray<MeshNode>> LiveNodesByRole { get; }
        = ImmutableDictionary<HostRole, ImmutableArray<MeshNode>>.Empty;

    // Indexer
    public MeshNode? this[NodeRef nodeRef] => AllNodes.GetValueOrDefault(nodeRef);

    public MeshState(MomentClock clock, ImmutableDictionary<NodeRef, MeshNode> allNodes, bool isFinal = false)
    {
        Clock = clock;
        AllNodes = allNodes;
        IsFinal = isFinal;
        if (allNodes.IsEmpty)
            return;

        AllRoles = AllNodes.Values.SelectMany(x => x.Roles).ToHashSet();
        LiveNodes = [..AllNodes.Values.Where(x => x.State.IsLive()).Order()];
        LiveNodesByRole = AllRoles.Select(r => new KeyValuePair<HostRole, ImmutableArray<MeshNode>>(
            r,
            [..LiveNodes.Where(n => n.Roles.Contains(r))])
        ).ToDictionary();
    }

    public override string ToString()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append("MeshState(").Append(AllNodes.Count).AppendLine(" node(s)) {");
        var now = Clock.Now;
        var i = 0;
        foreach (var node in AllNodes.Values.Order()) {
            sb.Append("  [").Append(i).Append("] = ").Append(node.LockKey).Append(": ").Append(node.State);
            if (node.DeadAt is { } deadAt)
                sb.Append($", dies in: {(deadAt - now).Positive().ToShortString()}");
            sb.AppendLine();
            i++;
        }
        sb.Append('}');
        return sb.ToStringAndRelease();
    }

    public MeshState ToFinal()
        => IsFinal
            ? this
            : new MeshState(Clock, AllNodes, isFinal: true);

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

            if (!LiveNodesByRole.TryGetValue(shardScheme.HostRole, out var nodes))
                nodes = ImmutableArray<MeshNode>.Empty;
            shardMap = new ShardMap(shardScheme, nodes);
            cache = cache == null ? new() : new Dictionary<ShardScheme, ShardMap>(cache);
            cache[shardScheme] = shardMap;
            _shardMapCache = cache;
        }
        return shardMap;
    }
}
