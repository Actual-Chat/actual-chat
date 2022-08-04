namespace ActualChat.Kvas;

public interface IKvas
{
    ValueTask<string?> Get(string key, CancellationToken cancellationToken = default);

    Task Set(string key, string? value, CancellationToken cancellationToken = default);
    Task SetMany((string Key, string? Value)[] items, CancellationToken cancellationToken = default);
}

public interface IKvas<TScope> : IKvas
{ }
