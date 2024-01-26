using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Mesh;

public sealed class MeshSharding
{
    private readonly MeshNode[] _doubleShards;

    public MeshShardingDef ShardingDef { get; }
    public int Size { get; }
    public IReadOnlyList<MeshNode> Nodes { get; }
    public ImmutableArray<MeshNode> Shards { get; }
    public bool IsValid => Nodes.Count != 0;
    // Indexers
    public MeshNode this[int index] => _doubleShards[Size + (index % Size)];
    public MeshNode this[string hash] => _doubleShards[Size + (hash.GetDjb2HashCode() % Size)];
    public MeshNode this[Symbol hash] => _doubleShards[Size + (hash.Value.GetDjb2HashCode() % Size)];

    public MeshSharding(MeshShardingDef shardingDef, IReadOnlyList<MeshNode> nodes)
    {
        ShardingDef = shardingDef;
        Size = shardingDef.Size;
        Nodes = nodes;
        _doubleShards = new MeshNode[Size * 2];
        if (IsValid) {
            var shardsPerNode = Math.Max(1, (double)Size / nodes.Count);
            for (var i = 0; i < _doubleShards.Length; i++) {
                var nodeIndex = (int)Math.Floor(1e-6 + (i % Size / shardsPerNode));
                _doubleShards[i] = nodes[nodeIndex];
            }
        }
        Shards = _doubleShards.Take(Size).ToImmutableArray();
    }

    public override string ToString()
    {
        var sb = StringBuilderExt.Acquire();
        sb.Append(nameof(MeshSharding));
        sb.Append('(').Append(ShardingDef).Append(" -> ");
        sb.Append(Nodes.Count).Append(' ').Append("node".Pluralize(Nodes.Count)).Append(") {");
        sb.AppendLine();
        if (IsValid) {
            var start = 0;
            foreach (var group in Shards.GroupBy(x => x)) {
                var count = group.Count();
                var end = start + count - 1;
                sb.Append("  [").Append(start).Append("..").Append(end).Append("] -> ").Append(group.Key.Id).AppendLine();
                start += count;
            }
        }
        else
            sb.Append("  [0..").Append(Size - 1).Append("] -> n/a").AppendLine();
        sb.Append('}');
        return sb.ToStringAndRelease();
    }
}
