using Stl.Rpc;

namespace ActualChat.Rpc;

public class RpcClientConnectionFactory
{
    private readonly IMutableState<int> _reconnectCount;

    public Stl.Rpc.RpcClientConnectionFactory Invoke { get; }
    public IState<int> ReconnectCount => _reconnectCount;

    public RpcClientConnectionFactory(IServiceProvider services)
    {
        _reconnectCount = services.StateFactory().NewMutable<int>();
        Invoke = async (peer, ct) => {
            var connection = await RpcDefaultDelegates.ClientConnectionFactory(peer, ct).ConfigureAwait(false);
            _reconnectCount.Value++;
            return connection;
        };
    }
}
