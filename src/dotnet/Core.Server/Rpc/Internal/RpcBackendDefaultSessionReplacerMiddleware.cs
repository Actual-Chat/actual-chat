using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Rpc.Internal;

public class RpcBackendDefaultSessionReplacerMiddleware : RpcInboundCallPreprocessor
{
    public override Func<RpcInboundCall, Task> CreateInboundCallPreprocessor(RpcMethodDef methodDef)
    {
        var arg0Type = methodDef.Parameters.GetValueOrDefault(0)?.ParameterType ?? typeof(Unit);
        if (arg0Type == typeof(Session))
            return call => {
                if (HasRpcBackendConnection(call, out var connection)) {
                    var args = call.Arguments!;
                    var session = (Session)args.Get0Untyped()!;
                    if (session.IsDefault()) {
                        session = connection.Session;
                        args.Set(0, session);
                    }
                    else
                        session.RequireValid();
                }
                return Task.CompletedTask;
            };

        if (typeof(ISessionCommand).IsAssignableFrom(arg0Type))
            return call => {
                if (HasRpcBackendConnection(call, out var connection)) {
                    var command = (ISessionCommand)call.Arguments!.Get0Untyped()!;
                    var session = command.Session;
                    if (session.IsDefault())
                        command.SetSession(connection.Session);
                    else
                        session.RequireValid();
                }
                return Task.CompletedTask;
            };

        return _ => Task.CompletedTask;
    }

    // Private methods

    private static bool HasRpcBackendConnection(
        RpcInboundCall call,
        [NotNullWhen(true)] out RpcBackendConnection? connection)
    {
        connection = call.Context.Peer.ConnectionState.Value.Connection as RpcBackendConnection;
        return connection != null;
    }
}
