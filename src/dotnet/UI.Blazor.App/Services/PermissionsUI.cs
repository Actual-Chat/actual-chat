using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public enum PermissionKind
{
    Microphone = 0,
    Notifications,
    Camera,
    Contacts,
    Location,
    BackgroundActivity,
    LockScreenCalls,
    LiveActivities,
}

/// <summary>
/// The catalog of OS permissions this app asks for on this platform, plus their grant state
/// and the account-level dismissal of the "some permissions are missing" warning.
/// </summary>
public sealed class PermissionsUI : UIServiceBase<AppUIHub>
{
    private readonly MutableState<int> _version;

    private BackgroundStateTracker BackgroundStateTracker
        => field ??= Services.GetRequiredService<BackgroundStateTracker>();

    public IReadOnlyList<PermissionDef> Permissions => field ??= CreatePermissions();

    public PermissionsUI(AppUIHub hub) : base(hub)
        => _version = hub.StateFactory.NewMutable(0, StateCategories.Get(GetType(), nameof(_version)));

    public async Task<PermissionsState> GetState(CancellationToken cancellationToken = default)
    {
        // Every Check below needs either JS interop or a platform bridge, and neither exists yet.
        if (IsPrerendering)
            return PermissionsState.Unknown;

        await _version.Use(cancellationToken).ConfigureAwait(false);
        // Nothing invalidates an OS-level permission read, so Use()-ing IsBackground is what re-reads
        // them on every return to the app - including the return from its own system settings page.
        await BackgroundStateTracker.IsBackground.Use(cancellationToken).ConfigureAwait(false);
        var isWarningDismissed = await UserSettingsUI.UserAppSettings()
            .Get(x => x.IsPermissionWarningDismissed ?? false, cancellationToken)
            .ConfigureAwait(false);

        var missing = new HashSet<PermissionKind>();
        foreach (var permission in Permissions) {
            var isGranted = await Check(permission, cancellationToken).ConfigureAwait(false);
            if (!isGranted)
                missing.Add(permission.Kind);
        }
        return new PermissionsState(missing, isWarningDismissed) { IsKnown = true };
    }

    public async Task<bool> Request(
        PermissionDef permission,
        bool mustTroubleshoot,
        CancellationToken cancellationToken = default)
    {
        try {
            return await permission.Request(mustTroubleshoot, cancellationToken).ConfigureAwait(false);
        }
        finally {
            Invalidate();
        }
    }

    public async Task DismissWarning(CancellationToken cancellationToken = default)
        => await UserSettingsUI.UserAppSettings()
            .Update(x => x with { IsPermissionWarningDismissed = true }, cancellationToken)
            .ConfigureAwait(false);

    public void Invalidate()
        => _version.Value++;

    // Private methods

    private async Task<bool> Check(PermissionDef permission, CancellationToken cancellationToken)
    {
        // A read can fail while the JS side or the platform bridge isn't ready yet; reporting
        // "granted" there keeps a transient failure from raising the warning badge.
        try {
            return await permission.Check(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Check failed for {Permission}", permission.Kind);
            return true;
        }
    }

    private IReadOnlyList<PermissionDef> CreatePermissions()
    {
        var appName = CoreConstants.AppName;
        var appKind = HostInfo.AppKind;
        var permissions = new List<PermissionDef>();

        var microphone = Services.GetRequiredService<MicrophonePermissionHandler>();
        permissions.Add(FromHandler(
            PermissionKind.Microphone,
            L.Permission_Microphone,
            L.Permission_MicrophoneRationale_Format(appName),
            "icon-mic",
            microphone,
            isInOnboarding: true));

        var notificationUI = Hub.NotificationUI;
        var notifications = Services.GetRequiredService<INotificationsPermission>();
        permissions.Add(new PermissionDef(
            PermissionKind.Notifications,
            L.Permission_Notifications,
            L.Permission_NotificationsRationale,
            "icon-bell") {
            Check = async ct => {
                var isGranted = await notificationUI.PermissionState.Use(ct).ConfigureAwait(false);
                if (isGranted == true)
                    return true;

                // PermissionState is read once at startup on MAUI, so re-reading the platform here
                // is what picks up a grant made in the system settings after that.
                isGranted = await notifications.IsGranted(ct).ConfigureAwait(false);
                notificationUI.SetIsGranted(isGranted);
                return isGranted == true;
            },
            Request = async (_, ct) => {
                await notifications.Request(ct).ConfigureAwait(false);
                return await notifications.IsGranted(ct).ConfigureAwait(false) == true;
            },
            // On the web the prompt must be bound to a direct user gesture in JS, which the
            // onboarding flow can't guarantee - the Settings page binds it to the toggle instead.
            IsInOnboarding = appKind.IsMobile(),
        });

        var camera = Services.GetRequiredService<CameraPermissionHandler>();
        permissions.Add(FromHandler(
            PermissionKind.Camera,
            L.Permission_Camera,
            L.Permission_CameraRationale,
            "icon-video",
            camera));

        // The web handler is a stub that always reports "granted" - there are no device contacts there.
        if (HostInfo.HostKind.IsMauiApp()) {
            var contacts = Services.GetRequiredService<ContactsPermissionHandler>();
            permissions.Add(FromHandler(
                PermissionKind.Contacts,
                L.Permission_Contacts,
                L.Permission_ContactsRationale_Format(appName),
                "icon-person",
                contacts));
        }

        var location = Services.GetRequiredService<LocationPermissionHandler>();
        permissions.Add(FromHandler(
            PermissionKind.Location,
            L.Permission_Location,
            L.Permission_LocationRationale,
            "icon-location-pin",
            location));

        // The handler is Android-only; iOS has no battery-optimization equivalent - APNs/PushKit
        // delivery isn't gated on a per-app battery setting.
        if (Services.GetService<BatteryOptimizationHandler>() is { } batteryOptimization)
            permissions.Add(FromHandler(
                PermissionKind.BackgroundActivity,
                L.Permission_BackgroundActivity,
                L.Permission_BackgroundActivityRationale_Format(appName),
                "icon-battery",
                batteryOptimization,
                isInOnboarding: true));

        if (appKind == AppKind.Android) {
            var fullScreenCalls = Services.GetRequiredService<IFullScreenCallsAvailability>();
            permissions.Add(new PermissionDef(
                PermissionKind.LockScreenCalls,
                L.Permission_LockScreenCalls,
                L.Permission_LockScreenCallsRationale_Format(appName),
                "icon-phone") {
                Check = async ct
                    => await fullScreenCalls.GetBlockedGate(ct).ConfigureAwait(false) == CallScreenGate.None,
                Request = async (_, ct) => {
                    var gate = await fullScreenCalls.GetBlockedGate(ct).ConfigureAwait(false);
                    await fullScreenCalls.OpenSettings(gate, ct).ConfigureAwait(false);
                    return false; // The grant happens in the system settings, not here
                },
            });
        }

        if (appKind == AppKind.Ios) {
            var liveActivities = Services.GetRequiredService<ILiveActivitiesAvailability>();
            var systemSettingsUI = Services.GetRequiredService<SystemSettingsUI>();
            permissions.Add(new PermissionDef(
                PermissionKind.LiveActivities,
                L.Permission_LiveActivities,
                L.Permission_LiveActivitiesRationale_Format(appName),
                "icon-notify-bell") {
                Check = liveActivities.IsEnabled,
                Request = async (_, ct) => {
                    await systemSettingsUI.Open().ConfigureAwait(false);
                    return false; // Same as above - the toggle lives in the system settings
                },
            });
        }
        return permissions;
    }

    private static PermissionDef FromHandler(
        PermissionKind kind,
        string title,
        string rationale,
        string icon,
        PermissionHandler handler,
        bool isInOnboarding = false)
        => new(kind, title, rationale, icon) {
            Check = async ct => await handler.Check(ct).ConfigureAwait(false) == true,
            Request = (mustTroubleshoot, ct) => handler.CheckOrRequest(true, mustTroubleshoot, ct).AsTask(),
            IsInOnboarding = isInOnboarding,
        };
}

public sealed record PermissionDef(
    PermissionKind Kind,
    string Title,
    string Rationale,
    string Icon)
{
    public required Func<CancellationToken, Task<bool>> Check { get; init; }
    public required Func<bool, CancellationToken, Task<bool>> Request { get; init; }
    public bool IsInOnboarding { get; init; }
}

public sealed record PermissionsState(IReadOnlySet<PermissionKind> Missing, bool IsWarningDismissed)
{
    public static readonly PermissionsState Unknown = new(new HashSet<PermissionKind>(), false);

    // False until the first read completes, so nothing renders "all granted" on the way there
    public bool IsKnown { get; init; }
    public bool HasMissing => Missing.Count != 0;

    public bool IsGranted(PermissionKind kind)
        => !Missing.Contains(kind);
}
