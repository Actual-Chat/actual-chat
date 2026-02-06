using System.Text;
using ActualLab.Scalability;

namespace ActualChat.Sharding;

/// <summary>
/// Maps shards to mesh nodes based on a sharding scheme.
/// </summary>
public sealed class ShardMap(ShardScheme shardScheme, MeshNode[] nodes)
    : ShardMap<MeshNode>(shardScheme.ShardCount, nodes, static node => node.GetHashes<int>())
{
    public ShardScheme ShardScheme { get; } = shardScheme;

    protected override void AppendToStringArguments(StringBuilder sb)
        => sb.Append(ShardScheme);
}
