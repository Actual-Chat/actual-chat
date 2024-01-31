using ActualChat.Hosting;

namespace ActualChat;

public sealed record ServerSideServiceDef(
    Type ServiceType,
    Type ImplementationType,
    HostRole ServerRole,
    ServiceMode ServiceMode)
{
    public override string ToString()
    {
        var prefix = ImplementationType == ServiceType
            ? ServiceType.GetName()
            : $"{ServiceType.GetName()} -> {ImplementationType.GetName()}";
        return $"{prefix} @ {ServerRole} as {ServiceMode:G}";
    }
};
