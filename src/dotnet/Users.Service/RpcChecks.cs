using ActualChat.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Users;

public class RpcChecks(IServiceProvider services) : IRpcChecks
{
    private RpcProbePolicy ProbePolicy { get; } = services.GetRequiredService<RpcProbePolicy>();

    public Task Ping(RelayRef relay, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<byte[]> GetPayload(RelayRef relay, int size, CancellationToken cancellationToken)
    {
        var context = RpcInboundContext.Current;
        return ProbePolicy.CreatePayload(context.GetRemoteIPAddress(), context.GetRequestHost(), size).AsTask();
    }
}
