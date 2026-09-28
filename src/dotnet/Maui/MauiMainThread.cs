using Microsoft.Maui.ApplicationModel;
#if MACOS
// MAUI Essentials' MainThread is its "not implemented" neutral build on the macos TFM
// TODO(maui-labs): see MacOSMainThread
using MainThread = ActualChat.Maui.MacOSMainThread;
#endif

namespace ActualChat.Maui;

/// <summary>
/// The main-thread API for every MAUI project: Essentials' <c>MainThread</c> behind an
/// <c>allowInline</c> switch, and the AppKit alias where Essentials has no implementation.
/// </summary>
public static class MauiMainThread
{
    public static bool IsMainThread => MainThread.IsMainThread;

    public static void BeginDispatchToMainThread(Action action, bool allowInline = true)
    {
        if (!allowInline && MainThread.IsMainThread)
            _ = Task.Run(() => MainThread.BeginInvokeOnMainThread(action));
        else
            MainThread.BeginInvokeOnMainThread(action);
    }

    public static Task DispatchToMainThread(Action action, bool allowInline = true)
        => !allowInline && MainThread.IsMainThread
            ? Task.Run(() => MainThread.InvokeOnMainThreadAsync(action))
            : MainThread.InvokeOnMainThreadAsync(action);

    public static Task<T> DispatchToMainThread<T>(Func<T> func, bool allowInline = true)
        => !allowInline && MainThread.IsMainThread
            ? Task.Run(() => MainThread.InvokeOnMainThreadAsync(func))
            : MainThread.InvokeOnMainThreadAsync(func);

    public static Task DispatchToMainThread(Func<Task> func, bool allowInline = true)
        => !allowInline && MainThread.IsMainThread
            ? Task.Run(() => MainThread.InvokeOnMainThreadAsync(func))
            : MainThread.InvokeOnMainThreadAsync(func);

    public static Task<T> DispatchToMainThread<T>(Func<Task<T>> func, bool allowInline = true)
        => !allowInline && MainThread.IsMainThread
            ? Task.Run(() => MainThread.InvokeOnMainThreadAsync(func))
            : MainThread.InvokeOnMainThreadAsync(func);
}
