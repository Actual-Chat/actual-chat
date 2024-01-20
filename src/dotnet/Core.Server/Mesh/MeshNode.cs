using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using MemoryPack;

namespace ActualChat.Mesh;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MeshNode(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Symbol Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Endpoint,
    [property: DataMember(Order = 2), MemoryPackOrder(2)] IReadOnlySet<HostRole> Roles
    ) : IComparable<MeshNode>
{
    private static readonly ListFormat Formatter = new(' ');

    public override string ToString()
        => Formatter.Format(Id.Value, Endpoint, Roles.ToDelimitedString(","));

    public static MeshNode Parse(string value)
        => TryParse(value, out var result) ? result : throw StandardError.Format<MeshNode>();

    public static bool TryParse(string value, [NotNullWhen(true)] out MeshNode? nodeInfo)
    {
        nodeInfo = null;
        var parser = Formatter.CreateParser(value);

        // Id
        if (!parser.TryParseNext())
            return false;
        var id = (Symbol)parser.Item;
        if (id.IsEmpty)
            return false;

        // Endpoint
        if (!parser.TryParseNext())
            return false;
        var endpoint = parser.Item;
        if (endpoint.IsNullOrEmpty())
            return false;

        // Roles
        var roles = Enumerable.Empty<HostRole>();
        if (parser.TryParseNext()) {
            roles = parser.Item.Split(',').Select(x => (HostRole)x);
            if (parser.TryParseNext())
                return false;
        }

        nodeInfo = new MeshNode(id, endpoint, new HashSet<HostRole>(roles));
        return true;
    }

    // Equality: uses only Id

    public bool Equals(MeshNode? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return Id.Equals(other.Id);
    }

    public override int GetHashCode() => Id.HashCode;

    // Comparison: uses only Id

    public int CompareTo(MeshNode? other)
        => ReferenceEquals(other, null)
            ? 1
            : StringComparer.Ordinal.Compare(Id.Value, other.Id.Value);

    public static bool operator <(MeshNode left, MeshNode right)
        => ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
    public static bool operator <=(MeshNode left, MeshNode right)
        => ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
    public static bool operator >(MeshNode left, MeshNode right)
        => !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
    public static bool operator >=(MeshNode left, MeshNode right)
        => ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
}
