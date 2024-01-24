using ActualChat.Hosting;

namespace ActualChat.Mesh;

#pragma warning disable CA1000

public abstract class MeshShardingDef(HostRole hostRole, int size)
{
    public HostRole HostRole { get; } = hostRole;
    public int Size { get; } = size;

    public override string ToString()
        => $"{nameof(Sharding)}.{HostRole}[{Size}]";
}

public interface IMeshShardingDef<out TSelf>
    where TSelf : MeshShardingDef, IMeshShardingDef<TSelf>
{
    public static abstract TSelf Instance { get; }
}
