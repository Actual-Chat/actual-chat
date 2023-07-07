using Microsoft.AspNetCore.Http;
using Stl.Rpc;
using Stl.Rpc.Infrastructure;

namespace ActualChat.Web.Services;

public class AppRpcConnectionFactory
{
    public string SessionParameterName { get; init; } = "session";

    public Task<RpcConnection> Invoke(
        RpcServerPeer peer, Channel<RpcMessage> channel, ImmutableOptionSet options,
        CancellationToken cancellationToken)
    {
        if (!options.TryGet<HttpContext>(out var httpContext))
            return RpcConnectionTask(channel, options);

        var session = SessionCookies.Read(httpContext, SessionParameterName);
        return session != null
            ? AppRpcConnectionTask(channel, options, session)
            : RpcConnectionTask(channel, options);
    }

    protected static Task<RpcConnection> AppRpcConnectionTask(
        Channel<RpcMessage> channel, ImmutableOptionSet options, Session session)
        => Task.FromResult<RpcConnection>(new AppRpcConnection(channel, options, session));

    protected static Task<RpcConnection> RpcConnectionTask(
        Channel<RpcMessage> channel, ImmutableOptionSet options)
        => Task.FromResult(new RpcConnection(channel, options));
}
