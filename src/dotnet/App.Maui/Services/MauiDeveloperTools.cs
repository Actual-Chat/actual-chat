using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.App.Maui.Services;

public class MauiDeveloperTools : IDeveloperTools
{
    private const string PreferenceKey = MauiDeveloperTools.PreferenceKeys.IsEnabled;
    private bool? _isEnabledCached;

    public bool IsEnabled(AccountFull account)
    {
        // Enables developer tools if at least once it was requested with Admin account.
        if (account.IsAdmin) {
            PersistIsEnabled();
            return true;
        }
        return GetIsEnabledPersisted();
    }

    private void PersistIsEnabled()
    {
        if (_isEnabledCached.GetValueOrDefault())
            return;

        Preferences.Default.Set(PreferenceKey, 1);
        _isEnabledCached = true;
    }

    private bool GetIsEnabledPersisted()
    {
        if (_isEnabledCached.HasValue)
            return _isEnabledCached.Value;

        var persistedValue = Preferences.Default.Get(PreferenceKey, 0);
        var isEnabled = persistedValue == 1;
        _isEnabledCached = isEnabled;
        return isEnabled;
    }

    // Nested classes

    public static class PreferenceKeys
    {
        private const string DeveloperToolsPrefix = "developer_tools.";
        public const string HostOverride = DeveloperToolsPrefix + "host_override";
        public const string IsEnabled = DeveloperToolsPrefix + "is_enabled";
    }
}
