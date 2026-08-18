using ActualChat.UI.Blazor.App.Services;
using Android.App;
using Android.Content;
using Android.Provider;
using Activity = Android.App.Activity;
using Application = Android.App.Application;
using JavaClass = Java.Lang.Class;
using JavaInteger = Java.Lang.Integer;
using JavaString = Java.Lang.String;
using Process = Android.OS.Process;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

/// <summary>
/// The two gates that can keep an incoming call off the screen: MIUI's own "show on lock screen"
/// app op, and Android 14+ <c>USE_FULL_SCREEN_INTENT</c>. Neither has a runtime-permission prompt.
/// </summary>
public sealed class AndroidFullScreenCallsAvailability(ILogger log) : IFullScreenCallsAvailability
{
    // Xiaomi's own app-op id; it isn't in AOSP, which is why it takes reflection to read.
    private const int OpShowWhenLocked = 10020;
    private const int ModeAllowed = 0;

    public Task<CallScreenGate> GetBlockedGate(CancellationToken cancellationToken = default)
    {
        // The stock gate goes first where it exists: its settings screen is the standard one, and
        // MIUI's own gate is only worth surfacing once that one is open. Below 34 only MIUI is left.
        if (OperatingSystem.IsAndroidVersionAtLeast(34) && !CanUseFullScreenIntent())
            return Task.FromResult(CallScreenGate.FullScreenIntent);
        if (IsMiui() && !IsLockScreenWindowAllowed())
            return Task.FromResult(CallScreenGate.LockScreenWindow);

        return Task.FromResult(CallScreenGate.None);
    }

    public Task OpenSettings(CallScreenGate gate, CancellationToken cancellationToken = default)
    {
        try {
            using var intent = gate switch {
                CallScreenGate.LockScreenWindow => NewMiuiPermissionsIntent(),
                CallScreenGate.FullScreenIntent => NewFullScreenIntentSettingsIntent(),
                _ => null,
            };
            if (intent is not null)
                StartActivity(intent);
        }
        catch (Exception e) {
            log.LogWarning(e, "Couldn't open the {Gate} settings, falling back to the app details", gate);
            TryOpenAppDetails();
        }

        return Task.CompletedTask;
    }

    // Private methods

    private bool CanUseFullScreenIntent()
    {
        // Without the manager there's nothing to report, and a stuck banner is worse.
        var notificationManager = (NotificationManager?)Application.Context
            .GetSystemService(Context.NotificationService);
        return notificationManager is null || notificationManager.CanUseFullScreenIntent();
    }

    private bool IsMiui()
    {
        if (!GetSystemProperty("ro.miui.ui.version.name").IsNullOrEmpty())
            return true;

        // HyperOS may drop the MIUI property while keeping the Security Center and its app ops.
        var manufacturer = Android.OS.Build.Manufacturer ?? "";
        return manufacturer.Equals("Xiaomi", StringComparison.OrdinalIgnoreCase)
            || manufacturer.Equals("Redmi", StringComparison.OrdinalIgnoreCase)
            || manufacturer.Equals("POCO", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLockScreenWindowAllowed()
    {
        // checkOpNoThrow's int overload is a non-SDK API, so this can fail outright on newer
        // Android - and then we report "allowed", because nagging on a guess is worse.
        try {
            var appOps = Application.Context.GetSystemService(Context.AppOpsService);
            if (appOps is null)
                return true;

            using var appOpsManagerClass = JavaClass.ForName("android.app.AppOpsManager");
            using var method = appOpsManagerClass.GetMethod("checkOpNoThrow",
                JavaInteger.Type!, JavaInteger.Type!, JavaClass.FromType(typeof(JavaString)));
            using var result = method!.Invoke(appOps,
                JavaInteger.ValueOf(OpShowWhenLocked),
                JavaInteger.ValueOf(Process.MyUid()),
                new JavaString(Application.Context.PackageName!));
            var isAllowed = result is JavaInteger mode && mode.IntValue() == ModeAllowed;
            log.LogInformation("MIUI OP_SHOW_WHEN_LOCKED: allowed={IsAllowed}, raw={Raw}", isAllowed, result);
            return isAllowed;
        }
        catch (Exception e) {
            log.LogWarning(e, "Couldn't read the MIUI OP_SHOW_WHEN_LOCKED app op");
            return true;
        }
    }

    private void TryOpenAppDetails()
    {
        try {
            using var intent = NewAppDetailsIntent();
            StartActivity(intent);
        }
        catch (Exception e) {
            log.LogWarning(e, "Couldn't open the app details settings either");
        }
    }

    private string? GetSystemProperty(string name)
    {
        try {
            using var systemPropertiesClass = JavaClass.ForName("android.os.SystemProperties");
            using var method = systemPropertiesClass.GetMethod("get", JavaClass.FromType(typeof(JavaString)));
            using var result = method!.Invoke(null, new JavaString(name));
            return result?.ToString();
        }
        catch (Exception e) {
            log.LogWarning(e, "Couldn't read the '{Name}' system property", name);
            return null;
        }
    }

    private static Intent NewFullScreenIntentSettingsIntent()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(34))
            return NewAppDetailsIntent();

        return new Intent(Settings.ActionManageAppUseFullScreenIntent, NewPackageUri());
    }

    private static Intent NewMiuiPermissionsIntent()
    {
        var intent = new Intent("miui.intent.action.APP_PERM_EDITOR");
        intent.SetPackage("com.miui.securitycenter");
        intent.PutExtra("extra_package_uid", Process.MyUid());
        intent.PutExtra("extra_pkgname", Application.Context.PackageName);
        return intent;
    }

    private static void StartActivity(Intent intent)
    {
        var context = Platform.CurrentActivity ?? (Context)Application.Context;
        if (context is not Activity)
            intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    private static Intent NewAppDetailsIntent()
        => new(Settings.ActionApplicationDetailsSettings, NewPackageUri());

    private static Uri NewPackageUri()
        => Uri.Parse("package:" + Application.Context.PackageName)!;
}
