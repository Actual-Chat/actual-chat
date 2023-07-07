using Stl.Fusion.Server.Rpc;
using Stl.Rpc.Infrastructure;

namespace ActualChat.Web;

public class AppRpcConnection : SessionBoundRpcConnection
{
    // Maybe add some extra properties later

    public AppRpcConnection(Channel<RpcMessage> channel, ImmutableOptionSet options, Session session)
        : base(channel, options, session)
    { }
}
