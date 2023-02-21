using AndroidX.Activity.Result;
using Object = Java.Lang.Object;

namespace ActualChat.App.Maui;

public class ActivityResultCallback : Object, IActivityResultCallback
{
    private readonly Action<ActivityResult> _callback;

    public ActivityResultCallback(Action<ActivityResult> callback)
        => _callback = callback;

    public ActivityResultCallback(TaskCompletionSource<ActivityResult> tcs)
        => _callback = tcs.SetResult;

    public void OnActivityResult(Object? p0) => _callback((ActivityResult)p0!);
}
