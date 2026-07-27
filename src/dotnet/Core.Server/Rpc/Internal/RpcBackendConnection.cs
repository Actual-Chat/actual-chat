using ActualLab.Fusion.Server.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Rpc.Internal;

public class RpcBackendConnection(
    RpcTransport transport,
    PropertyBag properties,
    Session session,
    string? remoteIPAddress)
    : SessionBoundRpcConnection(transport, properties, session)
{
    public string? RemoteIPAddress { get; init; } = remoteIPAddress;
}
