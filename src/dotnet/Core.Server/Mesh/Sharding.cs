using ActualChat.Hosting;

namespace ActualChat.Mesh;

public static class Sharding
{
    public sealed class Backend() : MeshShardingDef(HostRole.Backend, 32), IMeshShardingDef<Backend>
    {
        public static Backend Instance => new();
    }

    // A reverse map of HostRole to sharding definition
    public static readonly IReadOnlyDictionary<HostRole, MeshShardingDef> ByRole = new Dictionary<HostRole, MeshShardingDef>() {
        { HostRole.Backend, Backend.Instance },
    };
}
