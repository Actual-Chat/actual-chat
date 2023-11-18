using Microsoft.AspNetCore.Http;
using Stl.Rpc;
using Stl.Rpc.Infrastructure;

namespace ActualChat.Web.Services;

public class AppRpcServerConnectionFactory
{
#pragma warning disable CA1822 // Can be static
    public Task<RpcConnection> Invoke(
        RpcServerPeer peer, Channel<RpcMessage> channel, ImmutableOptionSet options,
        CancellationToken cancellationToken)
    {
        if (!options.TryGet<HttpContext>(out var httpContext))
            return RpcConnectionTask(channel, options);

        var session = httpContext.TryGetSessionFromHeader() ?? httpContext.TryGetSessionFromCookie();
        return session.IsValid()
            ? AppRpcConnectionTask(channel, options, session)
            : RpcConnectionTask(channel, options);
    }
#pragma warning restore CA1822

    protected static Task<RpcConnection> AppRpcConnectionTask(
        Channel<RpcMessage> channel, ImmutableOptionSet options, Session session)
        => Task.FromResult<RpcConnection>(new AppRpcConnection(channel, options, session));

    protected static Task<RpcConnection> RpcConnectionTask(
        Channel<RpcMessage> channel, ImmutableOptionSet options)
        => Task.FromResult(new RpcConnection(channel, options));
}
