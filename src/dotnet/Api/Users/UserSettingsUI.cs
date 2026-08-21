using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Session-bound wrapper around <see cref="IUserSettings"/>.
/// Works with <see cref="StoredSettings"/> instead of raw bytes.
/// When <see cref="Temporals"/> is available, Get checks for temporary overrides first,
/// and Set applies a temporary override alongside the server write.
/// </summary>
public sealed class UserSettingsUI(IServiceProvider services, Session session)
{
    public IServiceProvider Services { get; } = services;
    public IUserSettings UserSettings { get; } = services.GetRequiredService<IUserSettings>();
    public Temporals Temporals { get; } = services.Temporals();
    public Session Session { get; } = session;

    public async Task<StoredSettings?> Get(string key, CancellationToken cancellationToken = default)
    {
        if (KvasKeys.IsHidden(key))
            return null;

        if (Temporals.IsReal) {
            var value = await Temporals.Get<StoredSettings>(key).ConfigureAwait(false);
            if (value is not null)
                return value;
        }
        return await UserSettings.Get(Session, key, cancellationToken).ConfigureAwait(false);
    }

    public Task Set(string key, StoredSettings? value, CancellationToken cancellationToken = default)
    {
        KvasKeys.RequireNotHidden(key);
        if (Temporals.IsReal && value is not null)
            Temporals.Set(key, value);

        var command = new UserSettings_Set { Session = Session, Key = key, Value = value };
        return UserSettings.GetCommander().Call(command, true, cancellationToken);
    }
}
