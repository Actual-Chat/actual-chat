namespace ActualChat.Kvas;

public interface IKvas
{
    ValueTask<string?> Get(Symbol key, CancellationToken cancellationToken = default);
    void Set(Symbol key, string? value);
    Task Flush(CancellationToken cancellationToken = default);
}

public interface IKvas<TScope> : IKvas
{ }
