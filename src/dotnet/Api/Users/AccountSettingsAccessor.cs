namespace ActualChat.Users;

/// <summary>
/// Provides typed access to a single key in a <see cref="ScopedAccountSettings"/> store.
/// </summary>
public class AccountSettingsAccessor<T>(ScopedAccountSettings accountSettings, string key)
    where T : StoredSettings, new()
{
    public async Task<T> Get(CancellationToken cancellationToken = default)
    {
        var result = await accountSettings.Get(key, cancellationToken).ConfigureAwait(false);
        return (T?)result ?? new T();
    }

    public async Task<TResult> Get<TResult>(Func<T, TResult> selector, CancellationToken cancellationToken = default)
        => selector.Invoke(await Get(cancellationToken).ConfigureAwait(false));

    public Task Set(T value, CancellationToken cancellationToken = default)
        => accountSettings.Set(key, value, cancellationToken);

    public async Task<T> Update(Func<T, T> updater, CancellationToken cancellationToken = default)
    {
        var current = await Get(cancellationToken).ConfigureAwait(false);
        var newValue = updater.Invoke(current);
        await Set(newValue, cancellationToken).ConfigureAwait(false);
        return newValue;
    }
}
