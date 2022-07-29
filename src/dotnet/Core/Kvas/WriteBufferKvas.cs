namespace ActualChat.Kvas;

public abstract class WriteBufferKvas : IKvas
{
    public ValueTask<string?> Get(Symbol key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public void Set(Symbol key, string? value)
        => throw new NotImplementedException();

    public Task Flush(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
