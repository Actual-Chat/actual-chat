using ActualChat.Kvas;

namespace ActualChat.Core.UnitTests.Kvas;

public class TestKvasBackend : IKvasBackend
{
    private object Lock => Storage;

    public ITestOutputHelper? Out { get; init; }
    public Dictionary<Symbol, string> Storage { get; init; } = new();

    public Task<string?[]> GetMany(Symbol[] keys, CancellationToken cancellationToken = default)
    {
        lock (Lock) {
            Out?.WriteLine($"GetMany: {keys.ToDelimitedString(", ")}");
            var result = new string?[keys.Length];
            for (var i = 0; i < keys.Length; i++)
                result[i] = Storage.GetValueOrDefault(keys[i]);
            return Task.FromResult(result);
        }
    }

    public Task SetMany(List<(Symbol Key, string? Value)> updates, CancellationToken cancellationToken = default)
    {
        lock (Lock) {
            Out?.WriteLine($"SetMany: {updates.Select(u => $"({u.Key} = {u.Value})").ToDelimitedString(", ")}");
            foreach (var (key, value) in updates) {
                if (value == null)
                    Storage.Remove(key);
                else
                    Storage[key] = value;
            }
            return Task.CompletedTask;
        }
    }
}
