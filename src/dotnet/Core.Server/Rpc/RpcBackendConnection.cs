using ActualLab.Fusion.Server.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Rpc;

public class RpcBackendConnection(Channel<RpcMessage> channel, ImmutableOptionSet options, Session session)
    : SessionBoundRpcConnection(channel, options, session)
{
    // Maybe add some extra properties later
}
