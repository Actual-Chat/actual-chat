using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public sealed class MauiAppServerInstanceSelector(UIHub hub) : AppServerInstanceSelector
{
    private UIHub Hub { get; } = hub;
    private UrlMapper UrlMapper => Hub.UrlMapper;
    private ReloadUI ReloadUI => Hub.ReloadUI;

    public override AppServerInstance Default
        => field ??= AppServerInstance.TryCreate(MauiSettings.DefaultHost)!;

    public override AppServerInstance Current
        => field ??= AppServerInstance.TryCreate(UrlMapper.BaseUri.Host)!;

    public override AppServerInstance Get()
    {
        if (MauiPreferences.HostOverride is null)
            return Default;

        return AppServerInstance.TryCreate(MauiPreferences.HostOverride) ?? Default;
    }

    public override void Set(AppServerInstance instance)
    {
        var hostOverride = instance != Default ? instance : null;
        MauiPreferences.HostOverride = hostOverride?.HostName;
        // No cache clear: dropping the stored session means the next start mints a new one, and its
        // folder is a different one - the old server's entries are swept rather than reused.
        _ = MauiSession.RemoveStored().SuppressExceptions();
        _ = ReloadUI.Clear(clearLocalSettings: true);
    }
}
