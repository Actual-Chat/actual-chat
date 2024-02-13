namespace ActualChat;

public sealed record BackendServiceDef(
    Type ServiceType,
    Type ImplementationType,
    ServedByRoleSet ServedByRoles,
    ServiceMode ServiceMode)
{
    public override string ToString()
    {
        var prefix = ImplementationType == ServiceType
            ? ServiceType.GetName()
            : $"{ServiceType.GetName()} -> {ImplementationType.GetName()}";
        return $"{prefix} as {ServiceMode:G}, served by {ServedByRoles}";
    }
};
