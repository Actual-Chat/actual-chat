namespace ActualChat.Rpc;

/// <summary>
/// What only the platform knows. A client without UI reports one fixed network.
/// </summary>
public interface IRpcNetworkInfoProvider
{
    // The current network first, then every change. Null means offline or suspended: nothing can
    // be checked, so there is no point in trying to connect, and a failure then says nothing.
    IAsyncEnumerable<RpcNetworkInfo?> Watch(CancellationToken cancellationToken);
}
