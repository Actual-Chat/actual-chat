using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Extension methods for creating <see cref="SyncedState{T}"/> backed by <see cref="UserSettingsUI"/>.
/// </summary>
public static class StateFactoryExt
{
    public static SyncedState<T> NewUserSettingsSynced<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this StateFactory stateFactory,
        UserSettingsUI userSettingsUI,
        string key,
        T initialValue,
        IUpdateDelayer? updateDelayer = null,
        string? category = null,
        Func<CancellationToken, ValueTask<T>>? missingValueFactory = null)
        where T : StoredSettings, new()
    {
        var options = new SyncedState<T>.CustomOptions(
            Reader: async ct => {
                var result = await userSettingsUI.Get(key, ct).ConfigureAwait(false);
                if (result is null)
                    return missingValueFactory != null
                        ? await missingValueFactory.Invoke(ct).ConfigureAwait(false)
                        : initialValue;
                return (T?)result ?? initialValue;
            },
            Writer: (value, ct) => userSettingsUI.Set(key, value, ct)
        ) {
            InitialValue = initialValue,
            UpdateDelayer = updateDelayer,
            Category = category,
        };
        return new SyncedState<T>(options, stateFactory.Services);
    }
}
