using ActualChat.Maui.Services;
using ActualChat.UI.App.Services.NativeAppSettings;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

public static class MauiHostStorage
{
    private const string PreferenceKey = MauiDeveloperTools.PreferenceKeys.HostOverride;

    public static MauiHost? GetHostOverride()
    {
        var hostOverride = Preferences.Default.Get(PreferenceKey, "");
        if (hostOverride.IsNullOrEmpty())
            return null;

        var mauiHost = MauiHost.TryCreate(hostOverride);
        return mauiHost;
    }

    public static void SaveHostOverride(MauiHost? hostOverride)
    {
        if (hostOverride != null)
            Preferences.Default.Set(PreferenceKey, hostOverride.Host);
        else
            Preferences.Default.Remove(PreferenceKey);
    }
}
