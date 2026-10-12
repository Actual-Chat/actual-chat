namespace ActualChat.Maui;

// TODO(maui-labs): delete, with the MainThread alias in MauiMainThread, once Essentials'
// MainThread is implemented for the GTK backend.

/// <summary>
/// Main-thread dispatch for the GTK backend: it runs on the plain net TFM, where MAUI Essentials'
/// <c>MainThread</c> is the "not implemented" neutral build, so callers alias this instead.
/// </summary>
public static class LinuxMainThread
{
    public static bool IsMainThread
        // The GTK main loop owns the default main context for as long as it runs
        => GLib.Functions.MainContextDefault().IsOwner();

    public static Task InvokeOnMainThreadAsync(Action action)
        => InvokeOnMainThreadAsync(() => {
            action();
            return Task.FromResult(default(Unit));
        });

    public static Task<T> InvokeOnMainThreadAsync<T>(Func<T> func)
        => InvokeOnMainThreadAsync(() => Task.FromResult(func()));

    public static Task InvokeOnMainThreadAsync(Func<Task> funcTask)
        => InvokeOnMainThreadAsync(async () => {
            await funcTask().ConfigureAwait(false);
            return default(Unit);
        });

    public static Task<T> InvokeOnMainThreadAsync<T>(Func<Task<T>> funcTask)
    {
        if (IsMainThread)
            return funcTask();

        var whenDoneSource = TaskCompletionSourceExt.New<T>();
        BeginInvokeOnMainThread(() => _ = RunAndComplete());
        return whenDoneSource.Task;

        async Task RunAndComplete() {
            try {
                whenDoneSource.SetResult(await funcTask().ConfigureAwait(false));
            }
            catch (Exception e) {
                whenDoneSource.SetException(e);
            }
        }
    }

    public static void BeginInvokeOnMainThread(Action action)
    {
        if (IsMainThread)
            action();
        else
            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, () => {
                action();
                return false;
            });
    }
}
