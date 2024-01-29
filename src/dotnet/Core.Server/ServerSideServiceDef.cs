using ActualChat.Hosting;

namespace ActualChat;

public sealed record ServerSideServiceDef(
    Type ServiceType,
    Type ImplementationType,
    HostRole ServerRole,
    ServiceMode ServiceMode);
