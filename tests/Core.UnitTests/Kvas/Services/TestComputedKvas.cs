using ActualChat.Kvas;

namespace ActualChat.Core.UnitTests.Kvas.Services;

public class TestComputedKvas : IKvas, IComputeService
{
    private object Lock => Storage;

    public ITestOutputHelper? Out { get; init; }
    public Dictionary<Symbol, byte[]> Storage { get; init; } = new();

    [ComputeMethod]
    public virtual ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
    {
        lock (Lock)
            return ValueTask.FromResult(Storage.GetValueOrDefault(key));
    }

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
    {
        lock (Lock) {
            if (value == null)
                Storage.Remove(key);
            else
                Storage[key] = value;
        }
        using (ComputeContext.BeginInvalidation())
            _ = Get(key, default);
        return Task.CompletedTask;
    }

    public async Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
    {
        foreach (var (key, value) in items)
            await Set(key, value, cancellationToken).ConfigureAwait(false);
    }
}
