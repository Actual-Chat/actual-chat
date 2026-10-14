using ActualLab.Rpc;

namespace ActualChat.Rpc;

/// <summary>
/// What the path checks call. The first argument names the host the call goes through, so the call
/// reaches the peer made for that check. A call fails at once when that peer isn't connected,
/// and is never resent: a check that waits or retries would say nothing about the host.
/// </summary>
public interface IRpcChecks : IRpcService
{
    [RpcMethod(RemoteExecutionMode = 0)]
    Task Ping(RelayRef relay, CancellationToken cancellationToken);

    // Empty outside the countries where networks cap connections, unless the host is a relay
    [RpcMethod(RemoteExecutionMode = 0)]
    Task<byte[]> GetPayload(RelayRef relay, int size, CancellationToken cancellationToken);
}
