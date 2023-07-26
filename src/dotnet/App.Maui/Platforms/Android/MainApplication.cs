using Android.App;
using Android.Runtime;

namespace ActualChat.App.Maui;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
        => Android.Util.Log.Info(MauiDiagnostics.LogTag, "---- Started ----");

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
