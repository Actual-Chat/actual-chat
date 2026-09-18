using ActualChat.Localization;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Tells the UI how this client relates to its native counterpart: whether the store
/// (or, on the web, the server) has a newer build, and whether the app is already
/// installed on this device. Also takes the user to the store or into the app.
/// </summary>
public class AppUpdateUI(UIHub hub) : UIServiceBase<UIHub>(hub), IComputeService
{
    private const string AppKindQueryKey = "auAppKind";

    private static readonly TimeSpan ProbeRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly string JSGetInstalledAppIdsMethod
        = $"{BlazorUICoreModule.ImportName}.{nameof(AppUpdateUI)}.getInstalledAppIds";
    private static readonly string JSOpenAppMethod
        = $"{BlazorUICoreModule.ImportName}.{nameof(AppUpdateUI)}.openApp";

    private AppKind? _appKind;

    private IAppUpdates AppUpdates => field ??= Services.GetRequiredService<IAppUpdates>();
    private ReloadUI ReloadUI => Hub.ReloadUI;
    private ExternalUrlOpener ExternalUrlOpener => Hub.ExternalUrlOpener;
    private AppKind AppKind => _appKind ??= GetAppKind();
    // Both flavors are listed in the web manifest, so an installed dev app must not count on
    // voxt.ai and vice versa - they talk to different servers.
    private string AndroidAppId => UrlMapper.IsVoxt ? Constants.AppIds.Prod : Constants.AppIds.Dev;

    [ComputeMethod]
    public virtual async Task<AppUpdateInfo?> GetAvailableUpdate(CancellationToken cancellationToken)
    {
        // A server-side circuit (Blazor Server mode, prerendering) needs no update banner: a newer
        // server means this process is replaced, and the reconnect that follows reloads the page
        if (HostInfo.HostKind.IsServer())
            return null;

        var info = await AppUpdates.GetLatestUpdateInfo(AppKind, cancellationToken).ConfigureAwait(false);
        return info is not null && info.Version > ApiConstants.BuildVersion ? info : null;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsAppInstalled(CancellationToken cancellationToken)
    {
        // Only the web client has to ask: inside the app the answer is trivially "yes". Elsewhere
        // this is Android-only in practice - no other platform matches a "play" related application.
        if (HostInfo.HostKind == HostKind.MauiApp)
            return false;

        var appIds = await GetInstalledAppIds(cancellationToken).ConfigureAwait(false);
        if (appIds is null) {
            Computed.GetCurrent().Invalidate(ProbeRetryDelay);
            return false;
        }

        return appIds.Contains(AndroidAppId);
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

    public Task OpenApp()
    {
        // Chrome routes an intent:// URL to the package owning the matching App Link; a plain https
        // link wouldn't leave the browser. Opens at the root - that filter covers few path prefixes.
        var fallbackUrl = Uri.EscapeDataString(UrlMapper.BaseUrl);
        var url = $"intent://{UrlMapper.BaseUri.Host}/#Intent;scheme=https;package={AndroidAppId};"
            + $"S.browser_fallback_url={fallbackUrl};end";
        return JS.InvokeVoidAsync(JSOpenAppMethod, url).AsTask();
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

    // Null means we couldn't ask, so IsAppInstalled retries; an installed app found later in the
    // session needs a reload, since a successful answer is cached by the compute method for good.
    private async Task<string[]?> GetInstalledAppIds(CancellationToken cancellationToken)
    {
        if (IsPrerendering)
            return null;

        try {
            return await JS.InvokeAsync<string[]?>(JSGetInstalledAppIdsMethod, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "GetInstalledAppIds: probe failed");
            return null;
        }
    }
}
