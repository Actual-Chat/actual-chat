using ActualChat.Kvas;

namespace ActualChat.Core.UnitTests.Kvas.Services;

public class TestBatchingKvasBackend : IBatchingKvasBackend
{
    private object Lock => Storage;

    public ITestOutputHelper? Out { get; init; }
    public Dictionary<Symbol, byte[]> Storage { get; init; } = new();

    public ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        byte[]?[] result;
        lock (Lock) {
            Out?.WriteLine($"GetMany: {keys.ToDelimitedString(", ")}");
            result = new byte[]?[keys.Length];
            for (var i = 0; i < keys.Length; i++)
                result[i] = Storage.GetValueOrDefault(keys[i]);
        }
        var actualDelay = PreciseDelay.Delay(TimeSpan.FromMilliseconds(3));
        Out?.WriteLine("Actual delay: {0}", actualDelay.ToShortString());
        return ValueTask.FromResult(result);
    }

    public Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default)
    {
        lock (Lock) {
            if (Out != null) {
                var sUpdates = updates
                    .Select(u => $"({u.Key} = {(u.Value == null ? "null" : new TextOrBytes(u.Value))})")
                    .ToDelimitedString(", ");
                Out.WriteLine($"SetMany: {sUpdates}");
            }

            foreach (var (key, value) in updates) {
                if (value == null)
                    Storage.Remove(key);
                else
                    Storage[key] = value;
            }
            return Task.CompletedTask;
        }
    }

    public Task Clear(CancellationToken cancellationToken = default)
    {
        Out?.WriteLine("Clear");
        lock (Lock) {
            Storage.Clear();
        }
        return Task.CompletedTask;
    }
}
