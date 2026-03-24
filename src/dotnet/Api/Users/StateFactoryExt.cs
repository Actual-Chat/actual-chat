using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Extension methods for creating <see cref="SyncedState{T}"/> backed by <see cref="AccountSettingsUI"/>.
/// </summary>
public static class StateFactoryExt
{
    public static SyncedState<T> NewAccountSettingsSynced<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        this StateFactory stateFactory,
        AccountSettingsUI accountSettingsUI,
        string key,
        T initialValue,
        IUpdateDelayer? updateDelayer = null,
        string? category = null,
        Func<CancellationToken, ValueTask<T>>? missingValueFactory = null)
        where T : StoredSettings, new()
    {
        var options = new SyncedState<T>.CustomOptions(
            Reader: async ct => {
                var result = await accountSettingsUI.Get(key, ct).ConfigureAwait(false);
                if (result is null)
                    return missingValueFactory != null
                        ? await missingValueFactory.Invoke(ct).ConfigureAwait(false)
                        : initialValue;
                return (T?)result ?? initialValue;
            },
            Writer: (value, ct) => accountSettingsUI.Set(key, value, ct)
        ) {
            InitialValue = initialValue,
            UpdateDelayer = updateDelayer,
            Category = category,
        };
        return new SyncedState<T>(options, stateFactory.Services);
    }
}
