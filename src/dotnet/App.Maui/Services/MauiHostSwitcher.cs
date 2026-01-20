using ActualChat.Maui;
using ActualChat.Maui.Services;
using ActualChat.UI.App.Services.NativeAppSettings;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public class MauiHostSwitcher(UrlMapper urlMapper, ReloadUI reloadUI) : IMauiHostSwitcher
{
    private const string PreferenceKey = MauiDeveloperTools.PreferenceKeys.HostOverride;

    public MauiHost DefaultHost
        => field ??= MauiHost.TryCreate(MauiSettings.DefaultHost)!;

    public MauiHost CurrentHost
        => field ??= MauiHost.TryCreate(urlMapper.BaseUri.Host)!;

    public MauiHost GetHost()
        => MauiHostStorage.GetHostOverride() ?? DefaultHost;

    public void SetHost(MauiHost host)
    {
        var hostOverride = host != DefaultHost ? host : null;
        MauiHostStorage.SaveHostOverride(hostOverride);
        _ = MauiSession.RemoveStored().SuppressExceptions();
        _ = reloadUI.Clear(true, true);
    }
}
