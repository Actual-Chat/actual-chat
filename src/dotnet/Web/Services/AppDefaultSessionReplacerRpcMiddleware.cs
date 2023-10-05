using Stl.Rpc.Infrastructure;

namespace ActualChat.Web.Services;

public class AppDefaultSessionReplacerRpcMiddleware : RpcInboundMiddleware
{
    public AppDefaultSessionReplacerRpcMiddleware(IServiceProvider services) : base(services) { }

    public override Task OnBeforeCall(RpcInboundCall call)
    {
        var connection = call.Context.Peer.ConnectionState.Value.Connection as AppRpcConnection;
        if (connection == null)
            return Task.CompletedTask;

        var arguments = call.Arguments;
        var tItem0 = arguments!.GetType(0);
        if (tItem0 == typeof(Session)) {
            var session = arguments.Get<Session>(0);
            if (session.IsDefault()) {
                session = connection.Session;
                arguments.Set(0, session);
            }
            else
                session.RequireValid();
        }
        else if (typeof(ISessionCommand).IsAssignableFrom(tItem0)) {
            var command = arguments.Get<ISessionCommand>(0);
            var session = command.Session;
            if (session.IsDefault())
                command.SetSession(connection.Session);
            else
                session.RequireValid();
        }
        return Task.CompletedTask;
    }
}
