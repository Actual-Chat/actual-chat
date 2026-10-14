using System.Threading.Channels;
using ActualChat.Rpc;

namespace ActualChat.Api.Contracts.UnitTests.Rpc;

public sealed class FakeStore : IRpcPathStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, byte[]> _data = new();

    public int SaveCount { get; private set; }

    public void Seed(string networkId, RpcPathHistory history)
    {
        lock (_lock)
            _data[networkId] = history.ToBytes();
    }

    public RpcPathHistory GetHistory(string networkId)
    {
        lock (_lock)
            return RpcPathHistory.FromBytesOrEmpty(_data.GetValueOrDefault(networkId) ?? []);
    }

    public ValueTask<byte[]?> Load(string networkId, CancellationToken cancellationToken)
    {
        lock (_lock)
            return ValueTask.FromResult(_data.GetValueOrDefault(networkId));
    }

    public ValueTask Save(string networkId, byte[] data, CancellationToken cancellationToken)
    {
        lock (_lock) {
            _data[networkId] = data;
            SaveCount++;
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class FakeNetworkInfoProvider : IRpcNetworkInfoProvider
{
    private readonly Channel<RpcNetworkInfo?> _channel = Channel.CreateUnbounded<RpcNetworkInfo?>();

    public FakeNetworkInfoProvider()
        => _channel.Writer.TryWrite(new RpcNetworkInfo("net"));

    public void GoOffline()
        => _channel.Writer.TryWrite(null);

    public void ChangeNetwork(string networkId)
        => _channel.Writer.TryWrite(new RpcNetworkInfo(networkId));

    public IAsyncEnumerable<RpcNetworkInfo?> Watch(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

// A host answers at once when it has a ready result, and otherwise hangs until Complete
public sealed class FakeProbe : IRpcPathProbe
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, RpcPathMeasurement?> _ready = new();
    private readonly Dictionary<string, TaskCompletionSource<RpcPathMeasurement?>> _pending = new();
    private readonly Dictionary<string, string?> _attestations = new();
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls {
        get {
            lock (_lock)
                return [.._calls];
        }
    }

    public void Pass(string host, int pingMs = 80, int payloadMs = 300)
    {
        lock (_lock)
            _ready[host] = new RpcPathMeasurement(TimeSpan.FromMilliseconds(pingMs), TimeSpan.FromMilliseconds(payloadMs));
    }

    public void Fail(string host)
    {
        lock (_lock)
            _ready[host] = null;
    }

    public void Complete(string host, RpcPathMeasurement? result)
    {
        lock (_lock)
            GetPending(host).TrySetResult(result);
    }

    public Task<string?> GetAttestation(string host, CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult<string?>(_attestations.GetValueOrDefault(host));
    }

    public void SetAttestation(string host, string? attestation)
    {
        lock (_lock)
            _attestations[host] = attestation;
    }

    public Task<RpcPathMeasurement?> Check(string host, CancellationToken cancellationToken)
    {
        lock (_lock) {
            _calls.Add(host);
            return _ready.TryGetValue(host, out var result)
                ? Task.FromResult(result)
                : GetPending(host).Task.WaitAsync(cancellationToken);
        }
    }

    private TaskCompletionSource<RpcPathMeasurement?> GetPending(string host)
    {
        if (!_pending.TryGetValue(host, out var source))
            _pending[host] = source = TaskCompletionSourceExt.New<RpcPathMeasurement?>();
        return source;
    }
}
