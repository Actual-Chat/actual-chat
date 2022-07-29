namespace ActualChat.Kvas;

public interface IKvasBackend
{
    Task<string?[]> GetMany(Symbol[] keys, CancellationToken cancellationToken = default);
    Task SetMany(List<(Symbol Key, string? Value)> updates, CancellationToken cancellationToken = default);
}
