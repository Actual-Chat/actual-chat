using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public class MauiHostSwitcher(UrlMapper urlMapper, ReloadUI reloadUI) : IMauiHostSwitcher
{
    private const string PreferenceKey = MauiDeveloperTools.PreferenceKeys.HostOverride;
    private MauiHost? _defaultHost;
    private MauiHost? _currentHost;

    public MauiHost DefaultHost
        => _defaultHost ??= MauiHost.TryCreate(MauiSettings.DefaultHost)!;

    public MauiHost CurrentHost
        => _currentHost ??= MauiHost.TryCreate(urlMapper.BaseUri.Host)!;

    public MauiHost GetHost()
        => GetHostOverride() ?? DefaultHost;

    public void SetHost(MauiHost host)
    {
        var hostOverride = host != DefaultHost ? host : null;
        SaveHostOverride(hostOverride);
        _ = MauiSession.RemoveStored().SuppressExceptions();
        reloadUI.Clear(true, true);
    }

    public static MauiHost? GetHostOverride()
    {
        var hostOverride = Preferences.Default.Get(PreferenceKey, "");
        if (hostOverride.IsNullOrEmpty())
            return null;

        var mauiHost = MauiHost.TryCreate(hostOverride);
        return mauiHost;
    }

    private static void SaveHostOverride(MauiHost? hostOverride)
    {
        if (hostOverride != null)
            Preferences.Default.Set(PreferenceKey, hostOverride.Host);
        else
            Preferences.Default.Remove(PreferenceKey);
    }
}
