namespace ActualChat.Kvas;

public interface IKvas
{
    ValueTask<string?> Get(Symbol key, CancellationToken cancellationToken = default);
    Task Set(Symbol key, string? value, CancellationToken cancellationToken = default);
    Task Flush(CancellationToken cancellationToken = default);
}

public interface IKvas<TScope> : IKvas
{ }
