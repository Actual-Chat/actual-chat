using ActualChat.Hosting;

namespace ActualChat;

public sealed record BackendServiceDef(
    Type ServiceType,
    Type ImplementationType,
    ServiceMode ServiceMode,
    ShardScheme ShardScheme)
{
    public override string ToString()
    {
        var prefix = ImplementationType == ServiceType
            ? ServiceType.GetName()
            : $"{ServiceType.GetName()} -> {ImplementationType.GetName()}";
        return $"{prefix} as {ServiceMode:G}, shard scheme: {ShardScheme}";
    }
};
