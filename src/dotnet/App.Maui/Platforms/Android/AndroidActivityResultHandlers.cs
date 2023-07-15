using Android.Content;
using Activity = Android.App.Activity;
using Result = Android.App.Result;

namespace ActualChat.App.Maui;

public static class AndroidActivityResultHandlers
{
    private static event Action<Activity, int, Result, Intent?>? Handlers;

    public static void Register(Action<Activity, int, Result, Intent?> handler)
        => Handlers += handler;

    public static void Unregister(Action<Activity, int, Result, Intent?> handler)
        => Handlers -= handler;

    public static void Invoke(Activity activity, int requestCode, Result resultCode, Intent? data)
        => Handlers?.Invoke(activity, requestCode, resultCode, data);
}
