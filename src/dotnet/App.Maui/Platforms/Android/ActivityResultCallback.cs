using AndroidX.Activity.Result;
using Object = Java.Lang.Object;

namespace ActualChat.App.Maui;

public class ActivityResultCallback : Object, IActivityResultCallback
{
    private readonly Action<Object?> _callback;

    public ActivityResultCallback(Action<Object?> callback)
        => _callback = callback;

    public ActivityResultCallback(TaskCompletionSource<Object?> tcs)
        => _callback = tcs.SetResult;

    public void OnActivityResult(Object? p0) => _callback(p0);
}
