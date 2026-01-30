using ActualLab.Fusion.Server.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Rpc.Internal;

public class RpcBackendConnection(RpcTransport transport, PropertyBag properties, Session session)
    : SessionBoundRpcConnection(transport, properties, session)
{
    // Maybe add some extra properties later
}
