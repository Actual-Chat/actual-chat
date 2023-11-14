using Android.Content;
using Android.OS;
using Microsoft.Maui.LifecycleEvents;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public static class AndroidLifecycleLogger
{
    private static readonly Tracer _tracer = Tracer.Default[nameof(AndroidLifecycleLogger)];

    public static void Activate(IAndroidLifecycleBuilder android)
    {
        android.OnCreate(OnCreate);
        android.OnStart(OnStart);
        android.OnResume(OnResume);
        android.OnPause(OnPause);
        android.OnStop(OnStop);
        android.OnNewIntent(OnNewIntent);
        android.OnDestroy(OnDestroy);
    }

    private static void OnCreate(Activity activity, Bundle? savedInstanceState)
        => Trace();

    private static void OnStart(Activity activity)
        => Trace();

    private static void OnResume(Activity activity)
        => Trace();

    private static void OnPause(Activity activity)
        => Trace();

    private static void OnStop(Activity activity)
        => Trace();

    private static void OnNewIntent(Activity activity, Intent? intent)
        => Trace();

    private static void OnDestroy(Activity activity)
        => Trace();

    private static void Trace([CallerMemberName] string label = "")
        => _tracer.Point(label);
}
