namespace ActualChat.Kvas;

public interface IKvas
{
    ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default);
    ValueTask<ApiList<(string Key, byte[] Value)>> List(string keyPrefix, CancellationToken cancellationToken = default);


    Task Set(string key, byte[]? value, CancellationToken cancellationToken = default);
    Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default);
}

public interface IKvas<TScope> : IKvas;
