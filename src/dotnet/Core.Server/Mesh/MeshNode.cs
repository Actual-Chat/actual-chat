using System.Diagnostics.CodeAnalysis;
using ActualChat.Hashing;
using ActualChat.Hosting;
using MemoryPack;

namespace ActualChat.Mesh;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MeshNode(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] NodeRef Ref,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Endpoint,
    [property: DataMember(Order = 2), MemoryPackOrder(2)] IReadOnlySet<HostRole> Roles
    ) : IComparable<MeshNode>, IHasId<NodeRef>, IHasId<Symbol>
{
    private static readonly ListFormat Formatter = new(' ');

    private string? _toString;

    NodeRef IHasId<NodeRef>.Id => Ref;
    Symbol IHasId<Symbol>.Id => Ref.Id;

    public override string ToString()
        => _toString ??= Formatter.Format(Ref.Value, Endpoint, Roles.ToDelimitedString(","));

    public static MeshNode Parse(string value)
        => TryParse(value, out var result) ? result : throw StandardError.Format<MeshNode>();

    public static bool TryParse(string value, [NotNullWhen(true)] out MeshNode? nodeInfo)
    {
        nodeInfo = null;
        var parser = Formatter.CreateParser(value);

        // Id
        if (!parser.TryParseNext())
            return false;
        if (!NodeRef.TryParse(parser.Item, out var @ref))
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

        nodeInfo = new MeshNode(@ref, endpoint, new HashSet<HostRole>(roles));
        return true;
    }

    // Equality: uses only Ref

    public bool Equals(MeshNode? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        return ReferenceEquals(this, other) || Equals(Ref.Id, other.Ref.Id);
    }

    public override int GetHashCode() => Ref.Id.HashCode;

    public IEnumerable<T> GetHashes<T>()
        where T : unmanaged
    {
        for (var hashSource = Ref.Value;; hashSource += Ref.Value) {
            var hashes = hashSource.Hash().Blake2b().AsSpan<T>().ToArray();
            foreach (var hash in hashes)
                yield return hash;
        }
    }

    // Comparison: uses only Id

    public int CompareTo(MeshNode? other)
        => ReferenceEquals(other, null)
            ? 1
            : StringComparer.Ordinal.Compare(Ref.Value, other.Ref.Value);

    public static bool operator <(MeshNode left, MeshNode right)
        => ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
    public static bool operator <=(MeshNode left, MeshNode right)
        => ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
    public static bool operator >(MeshNode left, MeshNode right)
        => !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
    public static bool operator >=(MeshNode left, MeshNode right)
        => ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
}
