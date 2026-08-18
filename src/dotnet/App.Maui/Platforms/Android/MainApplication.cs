using ActualChat.App.Maui.Activities;
using Android.App;
using Android.Runtime;

namespace ActualChat.App.Maui;

#pragma warning disable // Can be static

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
        => Android.Util.Log.Info(MauiDiagnostics.LogTag, "---- Started ----");

    public override void OnCreate()
    {
        WarmUpWebView();
        base.OnCreate();
        // Any process start, not only a user launch. A message push starts us with no MainActivity
        // and no Blazor scope, so nothing else raises the armed service - leaving no PTT badge and
        // no media session for the headset button. Android refuses a background start without an
        // exemption; TryStart logs that rather than throwing.
        if (MauiPreferences.IsPttArmed)
            AndroidActivitiesForegroundService.TryStartArmed(this);
    }

    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();

    // Private methods

    private static void WarmUpWebView()
    {
        // Without this the WebView provider is loaded on the UI thread inside MainActivity's
        // performStart, right before BlazorAndroidWebView is constructed. GetDefaultUserAgent
        // forces the same load and is safe off the UI thread, moving ~10ms off the critical path.
        // Nothing waits on this: Chromium's own provider lock already makes WebView construction
        // block until the load finishes, and it posts its native init back to the UI thread - so
        // blocking the UI thread on this task instead would deadlock.
        _ = Task.Run(() => {
            try {
                _ = Android.Webkit.WebSettings.GetDefaultUserAgent(Android.App.Application.Context);
            }
            catch (Exception e) {
                Android.Util.Log.Warn(MauiDiagnostics.LogTag, $"WebView warm-up failed: {e.Message}");
            }
        });
    }
}
