namespace ActualChat.App.Maui;

public static class MainThreadExt
{
    public static void InvokeLater(Action action)
        // Thread pool scheduler -> Main thread
        => _ = Task.Run(() => MainThread.BeginInvokeOnMainThread(action));

    public static Task InvokeLaterAsync(Action action)
        // Thread pool scheduler -> Main thread
        => Task.Run(() => MainThread.InvokeOnMainThreadAsync(action));

    public static Task InvokeLaterAsync(Func<Task> action)
        // Thread pool scheduler -> Main thread
        => Task.Run(() => MainThread.InvokeOnMainThreadAsync(action));

    public static Task<T> InvokeLaterAsync<T>(Func<Task<T>> action)
        // Thread pool scheduler -> Main thread
        => Task.Run(() => MainThread.InvokeOnMainThreadAsync(action));
}
