namespace ActualChat.Kvas;

/// <summary>
/// An <see cref="IKvas"/> that owns its storage, so it can also be enumerated, flushed and wiped.
/// </summary>
public interface IKvasStore : IKvas
{
    ValueTask<(string Key, byte[] Value)[]> ListAllEntries(CancellationToken cancellationToken = default);
    Task Flush(CancellationToken cancellationToken = default);
    Task Clear(CancellationToken cancellationToken = default);
}
