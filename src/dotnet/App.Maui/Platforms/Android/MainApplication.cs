using ActualChat.App.Maui.Activities;
using Android.App;
using Android.Runtime;

namespace ActualChat.App.Maui;

#pragma warning disable // Can be static

[Application]
public sealed class MainApplication : MauiApplication, AndroidX.Work.Configuration.IProvider
{
    private static CpuTimestamp _startedAt;
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        _startedAt = CpuTimestamp.Now;
        Android.Util.Log.Info(MauiDiagnostics.LogTag, "---- Started ----");
    }

    public override void OnCreate()
    {
        base.OnCreate();
        // The moment the main looper is free to dispatch the broadcast that may have started us;
        // the delta from "CreateMauiApp completed" is pure MAUI framework overhead.
        MauiStartupBreadcrumbs.Add("Application.OnCreate completed");
        // AndroidMainThreadMonitor is blind to this dispatch - it is installed from inside it, so
        // the ">>>>>" it would pair with has already gone by. Same wording, so one grep finds both.
        if (MauiSettings.Diagnostics.EnableMainThreadMonitor)
            Android.Util.Log.Warn(MauiDiagnostics.LogTag,
                $"Main thread was blocked for {(CpuTimestamp.Now - _startedAt).ToShortString()}"
                + $" by: Application.onCreate ({MauiStart.Kind})");
        // Any process start, not only a user launch. A message push starts us with no MainActivity
        // and no Blazor scope, so nothing else raises the armed service - leaving no PTT badge and
        // no media session for the headset button. Android 12+ bans foreground-service starts from
        // the background outright, so there the attempt is skipped: user launches raise the service
        // in MainActivity while visible, and PTT wakes raise it inside the FCM exemption window.
        // The API check goes first - IsPttArmed forces the synchronous SharedPreferences load.
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) && MauiPreferences.IsPttArmed)
            AndroidActivitiesForegroundService.TryStartArmed(this);
    }

    // On-demand WorkManager init: AndroidManifest.xml removes WorkManagerInitializer to keep its
    // db + threads off the process-start path, and this keeps WorkManager.getInstance() working
    // for the transitive dependencies that may still call it.
    public AndroidX.Work.Configuration WorkManagerConfiguration
        => new AndroidX.Work.Configuration.Builder().Build();

    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();
}
