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

    public sealed class Default() : ShardScheme("Default", 0), IShardScheme<Default>
    {
        public static Default Instance { get; } = new();
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
    public bool IsDefault => ReferenceEquals(this, Default.Instance);

    public IEnumerable<int> ShardIndexes { get; } = Enumerable.Range(0, shardCount);
    public ImmutableArray<RpcPeerRef> BackendClientPeerRefs { get; }
        = Enumerable.Range(0, shardCount)
            .Select(shardIndex => RpcPeerRef.NewClient($"@shard-{id.Value}-{shardIndex}", true))
            .ToImmutableArray();

    public override string ToString()
        => $"{nameof(ShardScheme)}({Id}, {ShardCount})";

    public int GetShardIndex(int shardKey)
        => ShardCount <= 0 ? -1 : shardKey.Mod(ShardCount);

    public ShardScheme NonDefaultOr(ShardScheme shardScheme)
        => IsDefault ? shardScheme : this;
}

public interface IShardScheme<out TSelf>
    where TSelf : ShardScheme, IShardScheme<TSelf>
{
    public static abstract TSelf Instance { get; }
}
