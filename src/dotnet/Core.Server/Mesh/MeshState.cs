using ActualChat.Hosting;
using MemoryPack;

namespace ActualChat.Mesh;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MeshState
{
    public static readonly MeshState Empty = new();

    private readonly object _lock = new();
    private volatile Dictionary<ShardScheme, ShardMap>? _shardMapCache;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public ImmutableArray<MeshNode> Nodes { get; } = ImmutableArray<MeshNode>.Empty;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlySet<HostRole> Roles { get; } = ImmutableHashSet<HostRole>.Empty;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlyDictionary<HostRole, ImmutableArray<MeshNode>> NodesByRole { get; }
        = ImmutableDictionary<HostRole, ImmutableArray<MeshNode>>.Empty;

    public MeshState()
    { }

    [MemoryPackConstructor, Newtonsoft.Json.JsonConstructor]
    public MeshState(ImmutableArray<MeshNode> nodes)
    {
        Nodes = nodes;
        if (nodes.IsEmpty)
            return;

        Roles = Nodes.SelectMany(x => x.Roles).ToHashSet();
        NodesByRole = Roles.Select(r => new KeyValuePair<HostRole, ImmutableArray<MeshNode>>(
            r,
            Nodes.Where(n => n.Roles.Contains(r)).ToImmutableArray())
        ).ToDictionary();
    }

    public override string ToString()
    {
        var sb = StringBuilderExt.Acquire();
        sb.Append("MeshState(").Append(Nodes.Length).AppendLine(" node(s)) {");
        var i = 0;
        foreach (var node in Nodes) {
            sb.Append("  [").Append(i).Append("] = ").Append(node).AppendLine();
            i++;
        }
        sb.Append('}');
        return sb.ToStringAndRelease();
    }

    public ShardMap GetShardMap<TShardScheme>()
        where TShardScheme : ShardScheme, IShardScheme<TShardScheme>
        => GetShardMap(TShardScheme.Instance);

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

            if (!NodesByRole.TryGetValue(shardScheme.Id, out var nodes))
                nodes = ImmutableArray<MeshNode>.Empty;
            shardMap = new ShardMap(shardScheme, nodes);
            cache = cache == null ? new() : new Dictionary<ShardScheme, ShardMap>(cache);
            cache[shardScheme] = shardMap;
            _shardMapCache = cache;
        }
        return shardMap;
    }
}
