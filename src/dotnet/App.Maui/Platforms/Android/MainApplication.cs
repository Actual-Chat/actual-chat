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

    [Export, Lifecycle.Event.OnStop]
    public void OnAppBackgrounded()
    {
        Android.Util.Log.Info(MauiDiagnostics.LogTag, "OnAppBackgrounded");
        _ = DispatchToBlazor(
            c => c.GetRequiredService<IBackgroundStateHandler>().SetIsBackground(true),
            "IBackgroundStateHandler.SetBackgroundState(true)");
    }

    [Export, Lifecycle.Event.OnStart]
    public void OnAppForegrounded()
    {
        Android.Util.Log.Info(MauiDiagnostics.LogTag, "OnAppForegrounded");
        _ = DispatchToBlazor(
            c => c.GetRequiredService<IBackgroundStateHandler>().SetIsBackground(false),
            "IBackgroundStateHandler.SetBackgroundState(false)");
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
