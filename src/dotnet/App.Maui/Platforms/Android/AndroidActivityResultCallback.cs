using AndroidX.Activity.Result;
using Object = Java.Lang.Object;

namespace ActualChat.App.Maui;

public class AndroidActivityResultCallback(Action<Object?> callback) : Object, IActivityResultCallback
{
    public AndroidActivityResultCallback(TaskCompletionSource<Object?> tcs) : this(tcs.SetResult)
    { }

    public void OnActivityResult(Object? p0)
        => callback.Invoke(p0);
}
