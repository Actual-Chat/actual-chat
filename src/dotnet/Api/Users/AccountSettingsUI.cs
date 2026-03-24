namespace ActualChat.Users;

/// <summary>
/// Session-bound wrapper around <see cref="IAccountSettings"/>.
/// Works with <see cref="StoredSettings"/> instead of raw bytes.
/// When <see cref="Temporals"/> is available, Get checks for temporary overrides first,
/// and Set applies a temporary override alongside the server write.
/// </summary>
public sealed class AccountSettingsUI(IServiceProvider services, Session session)
{
    public IServiceProvider Services { get; } = services;
    public IAccountSettings AccountSettings { get; } = services.GetRequiredService<IAccountSettings>();
    public Temporals Temporals { get; } = services.Temporals();
    public Session Session { get; } = session;

    public async Task<StoredSettings?> Get(string key, CancellationToken cancellationToken = default)
    {
        if (Temporals.IsReal) {
            var value = await Temporals.Get<StoredSettings>(key).ConfigureAwait(false);
            if (value is not null)
                return value;
        }
        return await AccountSettings.Get(Session, key, cancellationToken).ConfigureAwait(false);
    }

    public Task Set(string key, StoredSettings? value, CancellationToken cancellationToken = default)
    {
        if (Temporals.IsReal && value is not null)
            Temporals.Set(key, value);

        var command = new AccountSettings_Set(Session, key, value);
        return AccountSettings.GetCommander().Call(command, true, cancellationToken);
    }
}
