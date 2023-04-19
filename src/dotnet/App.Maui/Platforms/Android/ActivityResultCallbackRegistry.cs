using Android.Content;
using Microsoft.Maui.Platform;
using Activity = Android.App.Activity;
using Result = Android.App.Result;

namespace ActualChat.App.Maui;

public static class ActivityResultCallbackRegistry
{
    static ActivityResultCallbackRegistry()
        => OnActivityResult += (_, _, _, _) => { };

    public static event Action<Activity, int, Result, Intent?> OnActivityResult;

    public static void InvokeCallback(Activity activity, int requestCode, Result resultCode, Intent? data)
        => OnActivityResult(activity, requestCode, resultCode, data);
}
