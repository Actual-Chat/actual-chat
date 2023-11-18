namespace ActualChat.Kvas;

public interface IBatchingKvasBackend
{
    ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default);
#pragma warning disable CA1002
    Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default);
#pragma warning restore CA1002
    Task Clear(CancellationToken cancellationToken = default);
}
