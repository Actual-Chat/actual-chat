namespace ActualChat.Kvas;

public interface IBatchingKvasBackend
{
    Task<string?[]> GetMany(string[] keys, CancellationToken cancellationToken = default);
    Task SetMany(List<(string Key, string? Value)> updates, CancellationToken cancellationToken = default);
}
