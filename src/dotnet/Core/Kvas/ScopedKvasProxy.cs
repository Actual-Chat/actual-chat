namespace ActualChat.Kvas;

public record struct ScopedKvasProxy<TScope>(IKvas Kvas) : IKvas<TScope>
{
    public ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
        => Kvas.Get(key, cancellationToken);

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
        => Kvas.Set(key, value, cancellationToken);

    public Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
        => Kvas.SetMany(items, cancellationToken);
}
