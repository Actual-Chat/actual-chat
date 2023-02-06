using Android.App;
using Android.Runtime;

namespace ActualChat.App.Maui;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        Android.Util.Log.Debug(AndroidConstants.LogTag, "****************************");
        Android.Util.Log.Debug(AndroidConstants.LogTag, "MainApplication. Constructor.");
        TraceSession.Main
            .ConfigureOutput(m => Android.Util.Log.Debug(AndroidConstants.LogTag, m))
            .Start()
            .Track("MainApplication.Constructor");
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
