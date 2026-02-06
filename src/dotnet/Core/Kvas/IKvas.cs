namespace ActualChat.Kvas;

/// <summary>
/// Key-value async store interface for persistent settings.
/// </summary>
public interface IKvas : IHasServices
{
    ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default);

    Task Set(string key, byte[]? value, CancellationToken cancellationToken = default);
    Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default);
}

/// <summary>
/// Scoped key-value async store interface.
/// </summary>
public interface IKvas<TScope> : IKvas;
