using ActualChat.Hosting;
using MemoryPack;

namespace ActualChat.Mesh;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MeshState
{
    public static readonly MeshState Empty = new();

    private readonly object _lock = new();
    private volatile Dictionary<MeshShardingDef, MeshSharding>? _shardingCache;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public ImmutableArray<MeshNode> Nodes { get; } = ImmutableArray<MeshNode>.Empty;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlySet<HostRole> Roles { get; private init; } = ImmutableHashSet<HostRole>.Empty;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlyDictionary<HostRole, ApiArray<MeshNode>> NodesByRole { get; private init; } = ImmutableDictionary<HostRole, ApiArray<MeshNode>>.Empty;

    public MeshState()
    { }

    [MemoryPackConstructor, Newtonsoft.Json.JsonConstructor]
    public MeshState(ImmutableArray<MeshNode> nodes)
    {
        Nodes = nodes;
        if (nodes.IsEmpty)
            return;

        Roles = Nodes.SelectMany(x => x.Roles).ToHashSet();
        NodesByRole = Roles.Select(r => new KeyValuePair<HostRole, ApiArray<MeshNode>>(
            r,
            new ApiArray<MeshNode>(Nodes.Where(n => n.Roles.Contains(r))))
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

    public MeshSharding GetSharding<TShardingDef>()
        where TShardingDef : MeshShardingDef, IMeshShardingDef<TShardingDef>
        => GetSharding(TShardingDef.Instance);

    public MeshSharding GetSharding(MeshShardingDef shardingDef)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var cache = _shardingCache;
        if (cache != null && cache.TryGetValue(shardingDef, out var sharding))
            return sharding;

        lock (_lock) { // Double-check locking
            cache = _shardingCache;
            if (cache != null && cache.TryGetValue(shardingDef, out sharding))
                return sharding;

            if (!NodesByRole.TryGetValue(shardingDef.HostRole, out var nodes))
                nodes = ApiArray<MeshNode>.Empty;
            sharding = new MeshSharding(shardingDef, nodes);
            cache = cache == null ? new() : new Dictionary<MeshShardingDef, MeshSharding>(cache);
            cache[shardingDef] = sharding;
            _shardingCache = cache;
        }
        return sharding;
    }
}
