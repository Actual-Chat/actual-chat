namespace ActualChat.Kvas;

/// <summary>
/// Wraps an <see cref="IKvas"/> to provide scoped access via <see cref="IKvas{TScope}"/>.
/// </summary>
public sealed record ScopedKvasProxy<TScope>(IKvas Kvas) : IKvas<TScope>
{
    public IServiceProvider Services => Kvas.Services;

    public ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
        => Kvas.Get(key, cancellationToken);

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
        => Kvas.Set(key, value, cancellationToken);

    public Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
        => Kvas.SetMany(items, cancellationToken);
}
