using ActualLab.Fusion.Server.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Rpc.Internal;

public class RpcBackendConnection(Channel<RpcMessage> channel, PropertyBag properties, Session session)
    : SessionBoundRpcConnection(channel, properties, session)
{
    // Maybe add some extra properties later
}
