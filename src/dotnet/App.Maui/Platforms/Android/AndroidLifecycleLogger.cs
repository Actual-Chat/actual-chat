using Android.Content;
using Android.OS;
using Microsoft.Maui.LifecycleEvents;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public static class AndroidLifecycleLogger
{
    private static readonly ILogger Log = StaticLog.For(typeof(AndroidLifecycleLogger));

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
        => Trace(activity, nameof(OnCreate) + $", Intent: '{Formatters.DumpIntent(activity.Intent)}'");

    private static void OnStart(Activity activity)
        => Trace(activity);

    private static void OnResume(Activity activity)
        => Trace(activity);

    private static void OnPause(Activity activity)
        => Trace(activity);

    private static void OnStop(Activity activity)
        => Trace(activity);

    private static void OnNewIntent(Activity activity, Intent? intent)
        => Trace(activity, nameof(OnNewIntent) + $", In-Intent: '{Formatters.DumpIntent(intent)}', Intent: '{Formatters.DumpIntent(activity.Intent)}'");

    private static void OnDestroy(Activity activity)
        => Trace(activity);

    private static void Trace(Activity activity, [CallerMemberName] string label = "")
    // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        => Log.LogInformation(activity.GetType().Name + ": " + label);
}
