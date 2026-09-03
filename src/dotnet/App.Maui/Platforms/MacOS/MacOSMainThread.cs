using CoreFoundation;
using Foundation;

namespace ActualChat.App.Maui;

// TODO(maui-labs): delete, with the MainThread alias in AppServicesAccessor, once Essentials'
// MainThread is implemented on the macos TFM.
/// <summary>
/// Main-thread dispatch for the AppKit backend: MAUI Essentials' <c>MainThread</c> resolves
/// to its "not implemented" neutral build on the macos TFM, so callers alias this instead.
/// </summary>
public static class MacOSMainThread
{
    public static bool IsMainThread => NSThread.Current.IsMainThread;

    public static void BeginInvokeOnMainThread(Action action)
    {
        if (IsMainThread)
            action();
        else
            DispatchQueue.MainQueue.DispatchAsync(action);
    }

    public static Task InvokeOnMainThreadAsync(Action action)
    {
        if (IsMainThread) {
            action();
            return Task.CompletedTask;
        }

        var whenDoneSource = TaskCompletionSourceExt.New();
        DispatchQueue.MainQueue.DispatchAsync(() => {
            try {
                action();
                whenDoneSource.SetResult();
            }
            catch (Exception e) {
                whenDoneSource.SetException(e);
            }
        });
        return whenDoneSource.Task;
    }

    public static Task<T> InvokeOnMainThreadAsync<T>(Func<T> func)
    {
        if (IsMainThread)
            return Task.FromResult(func());

        var whenDoneSource = TaskCompletionSourceExt.New<T>();
        DispatchQueue.MainQueue.DispatchAsync(() => {
            try {
                whenDoneSource.SetResult(func());
            }
            catch (Exception e) {
                whenDoneSource.SetException(e);
            }
        });
        return whenDoneSource.Task;
    }

    public static Task InvokeOnMainThreadAsync(Func<Task> funcTask)
    {
        if (IsMainThread)
            return funcTask();

        var whenDoneSource = TaskCompletionSourceExt.New();
        BeginInvokeOnMainThread(() => {
            _ = RunAndComplete();

            async Task RunAndComplete() {
                try {
                    await funcTask().ConfigureAwait(false);
                    whenDoneSource.SetResult();
                }
                catch (Exception e) {
                    whenDoneSource.SetException(e);
                }
            }
        });
        return whenDoneSource.Task;
    }

    public static Task<T> InvokeOnMainThreadAsync<T>(Func<Task<T>> funcTask)
    {
        if (IsMainThread)
            return funcTask();

        var whenDoneSource = TaskCompletionSourceExt.New<T>();
        BeginInvokeOnMainThread(() => {
            _ = RunAndComplete();

            async Task RunAndComplete() {
                try {
                    whenDoneSource.SetResult(await funcTask().ConfigureAwait(false));
                }
                catch (Exception e) {
                    whenDoneSource.SetException(e);
                }
            }
        });
        return whenDoneSource.Task;
    }
}
