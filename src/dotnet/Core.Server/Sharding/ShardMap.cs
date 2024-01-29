using ActualChat.Mesh;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat;

public sealed class ShardMap
{
    private readonly MeshNode[] _nodes2;
    private readonly int[] _shards2;

    public Sharding Sharding { get; }
    public ImmutableArray<MeshNode> Nodes { get; }
    public ImmutableArray<int> Shards { get; }
    public bool IsEmpty => Nodes.Length == 0;

    // Indexers
    public MeshNode? this[int hash] {
        get {
            var nodeIndex = GetNodeIndex(hash);
            return nodeIndex >= 0 ? _nodes2[nodeIndex] : null;
        }
    }

    public MeshNode? this[int hash, int nodeOffset] {
        get {
            var nodeIndex = GetNodeIndex(hash);
            return nodeIndex >= 0 ? _nodes2[Nodes.Length + ((nodeOffset + nodeIndex) % Nodes.Length)] : null;
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
        _nodes2 = Nodes.Concat(Nodes).ToArray();
        _shards2 = new int[shardCount * 2];
        if (nodeCount != 0) {
            var shardsPerNode = Math.Max(1, (double)shardCount / nodeCount);
            for (var i = 0; i < _shards2.Length; i++) {
                var nodeIndex = (int)Math.Floor(1e-6 + (i % shardCount / shardsPerNode));
                _shards2[i] = nodeIndex;
            }
        }
        else {
            for (var i = 0; i < _shards2.Length; i++)
                _shards2[i] = -1;
        }
        Shards = _shards2.Take(shardCount).ToImmutableArray();
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
            foreach (var group in Shards.GroupBy(x => x)) {
                var count = group.Count();
                var end = start + count - 1;
                var node = Nodes[group.Key];
                sb.Append("  [").Append(start).Append("..").Append(end).Append("] -> ").Append(node.Id).AppendLine();
                start += count;
            }
        }
        else
            sb.Append("  [0..").Append(Shards.Length - 1).Append("] -> n/a").AppendLine();
        sb.Append('}');
        return sb.ToStringAndRelease();
    }

    public int GetNodeIndex(int hash)
        => _shards2[Shards.Length + (hash % Shards.Length)];
}
