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
        Android.Util.Log.Debug(AndroidConstants.LogTag, "MainApplication.Constructor");
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
