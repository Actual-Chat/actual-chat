using Stl.Fusion.Server.Rpc;
using Stl.Rpc.Infrastructure;

namespace ActualChat.Web;

public class AppRpcConnection(Channel<RpcMessage> channel, ImmutableOptionSet options, Session session)
    : SessionBoundRpcConnection(channel, options, session)
{
    // Maybe add some extra properties later
}
