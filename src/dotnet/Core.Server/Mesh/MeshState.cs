using System.Text;
using ActualChat.Hosting;
using MemoryPack;

namespace ActualChat.Mesh;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MeshState
{
    public static readonly MeshState Empty = new();

    private readonly ImmutableArray<MeshNode> _nodes;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public ImmutableArray<MeshNode> Nodes {
        get => _nodes;
        init {
            _nodes = value;
            Roles = Nodes.SelectMany(x => x.Roles).ToHashSet();
            NodesByRole = Roles.Select(r => new KeyValuePair<HostRole, ApiArray<MeshNode>>(
                r,
                new ApiArray<MeshNode>(Nodes.Where(n => n.Roles.Contains(r))))
            ).ToDictionary();
        }
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlySet<HostRole> Roles { get; private init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlyDictionary<HostRole, ApiArray<MeshNode>> NodesByRole { get; private init; }

    public MeshState()
    {
        _nodes = ImmutableArray<MeshNode>.Empty;
        Roles = ImmutableHashSet<HostRole>.Empty;
        NodesByRole = ImmutableDictionary<HostRole, ApiArray<MeshNode>>.Empty;
    }

    [MemoryPackConstructor, Newtonsoft.Json.JsonConstructor]
    public MeshState(ImmutableArray<MeshNode> nodes) : this()
        => Nodes = nodes;

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

    // This record relies on referential equality
    public bool Equals(MeshState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
