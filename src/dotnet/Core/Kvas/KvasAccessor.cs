namespace ActualChat.Kvas;

public class KvasAccessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(IKvas kvas, string key) where T : class, new()
{
    public async Task<T> Get(CancellationToken cancellationToken = default)
        => await kvas.Get<T>(key, cancellationToken).ConfigureAwait(false) ?? new T();

    public async Task<TResult> Get<TResult>(Func<T, TResult> selector, CancellationToken cancellationToken = default)
        => selector(await kvas.Get<T>(key, cancellationToken).ConfigureAwait(false) ?? new T());

    public Task Set(T value, CancellationToken cancellationToken = default)
        => kvas.Set(key, value, cancellationToken);

    public async Task<T> Update(Func<T, T> update, CancellationToken cancellationToken = default)
    {
        var value = await kvas.Get<T>(key, cancellationToken).ConfigureAwait(false);
        var newValue = update.Invoke(value ?? new T());
        await kvas.Set(key, newValue, cancellationToken).ConfigureAwait(false);
        return newValue;
    }
}
