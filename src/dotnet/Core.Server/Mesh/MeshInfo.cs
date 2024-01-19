using ActualChat.Hosting;
using MemoryPack;

namespace ActualChat.Mesh;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MeshInfo
{
    public static readonly MeshInfo Empty = new();

    private readonly ImmutableArray<MeshNodeInfo> _nodes;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public ImmutableArray<MeshNodeInfo> Nodes {
        get => _nodes;
        init {
            _nodes = value;
            Roles = Nodes.SelectMany(x => x.Roles).ToHashSet();
            NodesByRole = Roles.Select(r => new KeyValuePair<HostRole, ApiArray<MeshNodeInfo>>(
                r,
                new ApiArray<MeshNodeInfo>(Nodes.Where(n => n.Roles.Contains(r))))
            ).ToDictionary();
        }
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlySet<HostRole> Roles { get; private init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlyDictionary<HostRole, ApiArray<MeshNodeInfo>> NodesByRole { get; private init; }

    public MeshInfo()
    {
        _nodes = ImmutableArray<MeshNodeInfo>.Empty;
        Roles = ImmutableHashSet<HostRole>.Empty;
        NodesByRole = ImmutableDictionary<HostRole, ApiArray<MeshNodeInfo>>.Empty;
    }

    public MeshInfo(params MeshNodeInfo[] nodes)
        : this(nodes.ToImmutableArray()) { }

    [MemoryPackConstructor, Newtonsoft.Json.JsonConstructor]
    public MeshInfo(ImmutableArray<MeshNodeInfo> nodes) : this()
        => Nodes = nodes;

    // This record relies on referential equality
    public bool Equals(MeshInfo? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
