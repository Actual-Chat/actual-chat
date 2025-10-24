using System.Text;
using ActualLab.Scalability;

namespace ActualChat.Sharding;

public sealed class ShardMap(ShardScheme shardScheme, ImmutableArray<MeshNode> nodes)
    : ShardMap<MeshNode>(shardScheme.ShardCount, nodes, static node => node.GetHashes<int>())
{
    public ShardScheme ShardScheme { get; } = shardScheme;

    protected override void AppendToStringArguments(StringBuilder sb)
        => sb.Append(ShardScheme);
}
