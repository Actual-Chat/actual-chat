using ActualChat.Kvas;

namespace ActualChat.Core.UnitTests.Kvas.Services;

public class TestComputedKvas : IKvas, IComputeService
{
    private readonly Dictionary<Symbol, int> _versions = new();

    private object Lock => Storage;

    public IServiceProvider Services => field ??= this.GetServices();

    public ITestOutputHelper? Out { get; init; }
    public Dictionary<Symbol, byte[]> Storage { get; init; } = new();
    public ConcurrentQueue<string> Journal { get; } = new();

    [ComputeMethod]
    public virtual ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
    {
        lock (Lock) {
            Journal.Enqueue($"get {key} -> v{_versions.GetValueOrDefault(key)}");
            return ValueTask.FromResult(Storage.GetValueOrDefault(key));
        }
    }

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
    {
        int version;
        lock (Lock) {
            if (value == null)
                Storage.Remove(key);
            else
                Storage[key] = value;
            version = _versions[key] = _versions.GetValueOrDefault(key) + 1;
            Journal.Enqueue($"set {key} v{version}");
        }
        using (Invalidation.Begin())
            _ = Get(key, default);
        Journal.Enqueue($"invalidated {key} after v{version}");
        return Task.CompletedTask;
    }

    public async Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
    {
        foreach (var (key, value) in items)
            await Set(key, value, cancellationToken).ConfigureAwait(false);
    }
}
