namespace ActualChat.Kvas;

public interface IBatchingKvasBackend
{
    ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default);
    Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default);
    Task Clear(CancellationToken cancellationToken = default);
}
