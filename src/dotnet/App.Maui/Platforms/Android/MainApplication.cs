using ActualChat.UI.Blazor.Services;
using Android.App;
using Android.Runtime;
using AndroidX.Lifecycle;
using Java.Interop;

namespace ActualChat.App.Maui;

#pragma warning disable // Can be static

[Application]
public class MainApplication : MauiApplication, ILifecycleObserver
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
        => Android.Util.Log.Info(MauiDiagnostics.LogTag, "---- Started ----");

    public override void OnCreate()
    {
        base.OnCreate();
        ProcessLifecycleOwner.Get().Lifecycle.AddObserver(this);
    }

    [Export, Lifecycle.Event.OnStart]
    public void OnBecameForeground()
    {
        Android.Util.Log.Info(MauiDiagnostics.LogTag, "OnBecameForeground");
        SetBackgroundState(false);
        if (MainPage.Current is { Content: null } mainPage)
            MainThread.BeginInvokeOnMainThread(() => mainPage.RecreateWebView());
    }

    [Export, Lifecycle.Event.OnStop]
    public void OnBecameBackground()
    {
        Android.Util.Log.Info(MauiDiagnostics.LogTag, "OnBecameBackground");
        SetBackgroundState(true);
    }

    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();

    private static void SetBackgroundState(bool isBackground)
        => _ = WhenAppServicesReady().ContinueWith(_ => {
            var t = (MauiBackgroundStateTracker)AppServices.GetRequiredService<BackgroundStateTracker>();
            t.IsBackground.Value = isBackground;
        }, TaskScheduler.Default);
}
