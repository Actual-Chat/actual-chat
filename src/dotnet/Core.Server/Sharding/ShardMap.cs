using ActualChat.Mesh;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

public sealed class ShardMap
{
    public Sharding Sharding { get; }
    public ImmutableArray<MeshNode> Nodes { get; }
    public ImmutableArray<int?> NodeIndexes { get; }
    public bool IsEmpty => Nodes.Length == 0;

    // Indexers
    public MeshNode? this[int hash] {
        get {
            var nodeIndex = NodeIndexes[Sharding.GetShardIndex(hash)];
            return nodeIndex.HasValue ? Nodes[nodeIndex.GetValueOrDefault()] : null;
        }
    }

    public MeshNode? this[int hash, int nodeOffset] {
        get {
            var nodeIndex = NodeIndexes[Sharding.GetShardIndex(hash)];
            return nodeIndex.HasValue ? Nodes.GetRingItem(nodeOffset + nodeIndex.GetValueOrDefault()) : null;
        }
    }

    public MeshNode? this[string hash] => this[hash.GetDjb2HashCode()];
    public MeshNode? this[string hash, int nodeOffset] => this[hash.GetDjb2HashCode(), nodeOffset];

    public ShardMap(Sharding sharding, ImmutableArray<MeshNode> nodes)
    {
        Sharding = sharding;
        Nodes = nodes;
        var nodeCount = nodes.Length;
        var shardCount = Sharding.ShardCount;
        var shards = new int?[shardCount];
        if (nodeCount != 0) {
            var shardsPerNode = Math.Max(1, (double)shardCount / nodeCount);
            for (var i = 0; i < shards.Length; i++) {
                var nodeIndex = (int)Math.Floor(1e-6 + (i % shardCount / shardsPerNode));
                shards[i] = nodeIndex;
            }
        }
        NodeIndexes = shards.ToImmutableArray();
    }

    public override string ToString()
    {
        var sb = StringBuilderExt.Acquire();
        sb.Append(nameof(ShardMap));
        sb.Append('(').Append(Sharding).Append(" -> ");
        sb.Append(Nodes.Length).Append(' ').Append("node".Pluralize(Nodes.Length)).Append(") {");
        sb.AppendLine();
        if (!IsEmpty) {
            var start = 0;
            foreach (var group in NodeIndexes.GroupBy(x => x)) {
                var count = group.Count();
                var end = start + count - 1;
                var node = Nodes[(int)group.Key!];
                sb.Append("  [").Append(start).Append("..").Append(end).Append("] -> ").Append(node.Id).AppendLine();
                start += count;
            }
        }
        else
            sb.Append("  [0..").Append(NodeIndexes.Length - 1).Append("] -> n/a").AppendLine();
        sb.Append('}');
        return sb.ToStringAndRelease();
    }
}
