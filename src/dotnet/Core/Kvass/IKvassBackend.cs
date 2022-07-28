namespace ActualChat.Kvass;

public interface IKvassBackend
{
    Task<string?[]> GetMany(string[] keys, CancellationToken cancellationToken = default);
    Task SetMany((string Key, string? Value)[] updates, CancellationToken cancellationToken = default);

    event Action<string[]> Changed;
}
