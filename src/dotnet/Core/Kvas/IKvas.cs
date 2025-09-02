namespace ActualChat.Kvas;

public interface IKvas : IHasServices
{
    ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default);

    Task Set(string key, byte[]? value, CancellationToken cancellationToken = default);
    Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default);
}

public interface IKvas<TScope> : IKvas;

public interface IKvas2 : IKvas
{
    ValueTask<(string Key, byte[] Value)[]> GetAll(CancellationToken cancellationToken = default);
}
