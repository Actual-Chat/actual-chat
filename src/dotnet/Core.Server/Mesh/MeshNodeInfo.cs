using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using MemoryPack;

namespace ActualChat.Mesh;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record MeshNodeInfo(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Symbol Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Endpoint,
    [property: DataMember(Order = 2), MemoryPackOrder(2)] IReadOnlySet<HostRole> Roles
) {
    private static readonly ListFormat Format = new(' ');

    public override string ToString()
        => Format.Format(Id.Value, Endpoint, Roles.ToDelimitedString(","));

    public static MeshNodeInfo Parse(string value)
        => TryParse(value, out var result) ? result : throw StandardError.Format<MeshNodeInfo>();

    public static bool TryParse(string value, [NotNullWhen(true)] out MeshNodeInfo? nodeInfo)
    {
        nodeInfo = null;
        var parser = Format.CreateParser(value);

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
        if (!parser.TryParseNext())
            return false;
        var roles = parser.Item.Split(',').Select(x => (HostRole)x);
        if (parser.TryParseNext())
            return false;

        nodeInfo = new MeshNodeInfo(id, endpoint, new HashSet<HostRole>(roles));
        return true;
    }
}
