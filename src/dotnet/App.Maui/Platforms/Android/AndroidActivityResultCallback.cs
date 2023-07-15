using AndroidX.Activity.Result;
using Object = Java.Lang.Object;

namespace ActualChat.App.Maui;

public class AndroidActivityResultCallback : Object, IActivityResultCallback
{
    private readonly Action<Object?> _callback;

    public AndroidActivityResultCallback(Action<Object?> callback)
        => _callback = callback;

    public AndroidActivityResultCallback(TaskCompletionSource<Object?> tcs)
        => _callback = tcs.SetResult;

    public void OnActivityResult(Object? p0)
        => _callback.Invoke(p0);
}
