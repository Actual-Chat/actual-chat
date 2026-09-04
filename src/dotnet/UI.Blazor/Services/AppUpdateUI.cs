using ActualChat.Localization;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Tells the UI whether the store (or, on the web, the server) has a newer build than
/// the one this client runs, and takes the user there.
/// </summary>
public class AppUpdateUI(UIHub hub) : UIServiceBase<UIHub>(hub), IComputeService
{
    private const string AppKindQueryKey = "auAppKind";

    private AppKind? _appKind;

    private IAppUpdates AppUpdates => field ??= Services.GetRequiredService<IAppUpdates>();
    private ReloadUI ReloadUI => Hub.ReloadUI;
    private ExternalUrlOpener ExternalUrlOpener => Hub.ExternalUrlOpener;
    private AppKind AppKind => _appKind ??= GetAppKind();

    [ComputeMethod]
    public virtual async Task<AppUpdateInfo?> GetAvailableUpdate(CancellationToken cancellationToken)
    {
        // A server-side circuit (Blazor Server mode, prerendering) needs no update banner: a newer
        // server means this process is replaced, and the reconnect that follows reloads the page
        if (HostInfo.HostKind.IsServer())
            return null;

        var info = await AppUpdates.GetLatestUpdateInfo(AppKind, cancellationToken).ConfigureAwait(false);
        if (info is null || !VersionExt.TryParseBuildVersion(info.Version, out var latestVersion))
            return null;

        return latestVersion > ApiConstants.BuildVersion ? info : null;
    }

    public async Task Update()
    {
        if (Links.Apps.Store(AppKind) is { } storeUrl) {
            await ExternalUrlOpener.Open(storeUrl).ConfigureAwait(false);
            return;
        }

        // The web app's store is the server it's already talking to, so an update is a reload.
        // It must start after the modal is gone: closing it pops a history entry, and that
        // same-document navigation cancels a reload issued from inside the confirm handler.
        var isConfirmed = false;
        var model = new ConfirmModal.Model(false, L.AppUpdate_ReloadText, () => isConfirmed = true) {
            Title = L.AppUpdate_ReloadTitle_Format(CoreConstants.AppName),
            ConfirmButtonText = L.Common_Update,
        };
        var modalRef = await ModalUI.Show(model).ConfigureAwait(true);
        await modalRef.WhenClosed.ConfigureAwait(true);
        if (!isConfirmed)
            return;

        await History.WhenNavigationCompletedOrTimeout().ConfigureAwait(true);
        ReloadUI.Reload();
    }

    // Private methods

    private AppKind GetAppKind()
    {
        // ?auAppKind=Android makes this client ask for, and link to, that kind's store - a QA hook
        // for the banner and its click paths, so it's off on production instances
        if (HostInfo.IsProductionInstance)
            return HostInfo.AppKind;

        var forcedAppKind = new Uri(Nav.Uri).GetQueryCollection()[AppKindQueryKey];
        return Enum.TryParse<AppKind>(forcedAppKind, ignoreCase: true, out var appKind) && Enum.IsDefined(appKind)
            ? appKind
            : HostInfo.AppKind;
    }
}
