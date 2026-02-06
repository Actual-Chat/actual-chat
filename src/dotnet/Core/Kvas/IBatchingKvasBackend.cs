namespace ActualChat.Kvas;

/// <summary>
/// Backend interface for batched key-value store operations.
/// </summary>
public interface IBatchingKvasBackend
{
    ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default);
    ValueTask<(string Key, byte[] Value)[]> ListAllEntries(CancellationToken cancellationToken = default);
    Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default);
    Task Clear(CancellationToken cancellationToken = default);
}
