using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat;

#pragma warning disable CA1000

public abstract class Sharding(HostRole hostRole, int shardCount)
{
    public sealed class Backend() : Sharding(HostRole.BackendServer, 10), ISharding<Backend>
    {
        public static Backend Instance => new();
    }

    // A reverse map of HostRole to sharding definition
    public static readonly IReadOnlyDictionary<HostRole, Sharding> ByRole = new Dictionary<HostRole, Sharding>() {
        { HostRole.BackendServer, Backend.Instance },
    };

    public HostRole HostRole { get; } = hostRole;
    public int ShardCount { get; } = shardCount;
    public IEnumerable<int> ShardIndexes { get; } = Enumerable.Range(0, shardCount);
    public ImmutableArray<RpcPeerRef> ClientPeerRefs { get; }
        = Enumerable.Range(0, shardCount).Select(x => RpcPeerRef.NewClient($"backend-{x}")).ToImmutableArray();

    public int GetShardIndex(int hash)
    {
        var index = hash % ShardCount;
        return index >= 0 ? index : index + ShardCount;
    }

    public RpcPeerRef GetClientPeerRef(int hash)
        => ClientPeerRefs[GetShardIndex(hash)];

    public override string ToString()
        => $"{nameof(Sharding)}.{HostRole}[{ShardCount}]";
}

public interface ISharding<out TSelf>
    where TSelf : Sharding, ISharding<TSelf>
{
    public static abstract TSelf Instance { get; }
}
