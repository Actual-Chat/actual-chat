using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat;

#pragma warning disable CA1000

public abstract class ShardScheme(Symbol id, int shardCount) : IHasId<Symbol>
{
    public sealed class None() : ShardScheme(Symbol.Empty, 0), IShardScheme<None>
    {
        public static None Instance { get; } = new();
    }

    public sealed class Undefined() : ShardScheme("Undefined", 0), IShardScheme<Undefined>
    {
        public static Undefined Instance { get; } = new();
    }

    public sealed class Backend() : ShardScheme(HostRole.BackendServer.Id, 10), IShardScheme<Backend>
    {
        public static Backend Instance { get; } = new();
    }

    // A reverse map of ShardScheme.Id to ShardScheme
    public static readonly IReadOnlyDictionary<Symbol, ShardScheme> ById = new Dictionary<Symbol, ShardScheme>() {
        { None.Instance.Id, Backend.Instance },
        { Backend.Instance.Id, Backend.Instance },
    };

    public Symbol Id { get; } = id;
    public int ShardCount { get; } = shardCount;
    public bool IsNone => ReferenceEquals(this, None.Instance);
    public bool IsUndefined => ReferenceEquals(this, Undefined.Instance);

    public IEnumerable<int> ShardIndexes { get; } = Enumerable.Range(0, shardCount);
    public ImmutableArray<RpcPeerRef> BackendClientPeerRefs { get; }
        = Enumerable.Range(0, shardCount)
            .Select(shardIndex => RpcPeerRef.NewClient($"@shard-{id.Value}-{shardIndex}", true))
            .ToImmutableArray();

    public int GetShardIndex(int shardKey)
        => ShardCount <= 0 ? -1 : shardKey.Mod(ShardCount);

    public override string ToString()
        => $"{nameof(ShardScheme)}({Id}, {ShardCount})";
}

public interface IShardScheme<out TSelf>
    where TSelf : ShardScheme, IShardScheme<TSelf>
{
    public static abstract TSelf Instance { get; }
}
