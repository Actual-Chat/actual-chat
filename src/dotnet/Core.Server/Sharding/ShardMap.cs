using ActualChat.Mesh;

namespace ActualChat;

public sealed class ShardMap
{
    public ShardScheme ShardScheme { get; }
    public ImmutableArray<MeshNode> Nodes { get; }
    public ImmutableArray<int?> NodeIndexes { get; }
    public bool IsEmpty => Nodes.Length == 0;

    // Indexers
    public MeshNode? this[int shardIndex] {
        get {
            var nodeIndex = NodeIndexes[shardIndex];
            return nodeIndex.HasValue ? Nodes[nodeIndex.GetValueOrDefault()] : null;
        }
    }

    public MeshNode? this[int shardIndex, int nodeOffset] {
        get {
            var nodeIndex = NodeIndexes[shardIndex];
            return nodeIndex.HasValue ? Nodes.GetRingItem(nodeOffset + nodeIndex.GetValueOrDefault()) : null;
        }
    }

    public ShardMap(ShardScheme shardScheme, ImmutableArray<MeshNode> nodes)
    {
        ShardScheme = shardScheme;
        Nodes = nodes;
        var remainingNodeCount = nodes.Length;
        var shardCount = ShardScheme.ShardCount;
        var shards = new int?[shardCount];
        var remainingShardCount = shardCount;
        while (remainingNodeCount != 0) {
            var nodeIndex = nodes.Length - remainingNodeCount;
            var node = nodes[nodeIndex];
            var nodeShardCount = (remainingShardCount + remainingNodeCount - 1) / remainingNodeCount;
            foreach (var hash in node.GetHashes<int>().Take(nodeShardCount)) {
                for (var i = 0; i < shardCount; i++) {
                    ref var shard = ref shards[(hash + i).Mod(shardCount)];
                    if (!shard.HasValue) {
                        shard = nodeIndex;
                        break;
                    }
                }
            }
            remainingShardCount -= nodeShardCount;
            remainingNodeCount--;
        }
        NodeIndexes = shards.ToImmutableArray();
    }

    public override string ToString()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append(nameof(ShardMap));
        sb.Append('(').Append(ShardScheme).Append(" -> ");
        sb.Append(Nodes.Length).Append(' ').Append("node".Pluralize(Nodes.Length)).Append(')');
        if (IsEmpty)
            return sb.ToStringAndRelease();

        sb.AppendLine(" {");
        for (var nodeIndex = 0; nodeIndex < Nodes.Length; nodeIndex++) {
            var node = Nodes[nodeIndex];
            sb.Append("  ").Append(node.Ref).Append(": ");
            foreach (var shardNodeIndex in NodeIndexes)
                sb.Append(shardNodeIndex == nodeIndex ? '\u25cf' : '•');
            sb.AppendLine();
        }
        sb.Append('}');
        return sb.ToStringAndRelease();
    }
}
