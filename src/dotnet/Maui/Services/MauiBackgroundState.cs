namespace ActualChat.Maui.Services;

/// <summary>
/// Static background state shared across the Maui layer.
/// Set by platform-specific code (AppDelegate on iOS, lifecycle events on Android/Windows).
/// Consumed by <see cref="SQLiteBatchingKvasBackend"/> to auto-suspend when backgrounded.
/// </summary>
public static class MauiBackgroundState
{
    private static readonly Lock Lock = new();
    private static readonly List<Action> SuspendHandlers = [];
    private static readonly MutableState<bool> IsBackgroundMutable
        = StateFactory.Default.NewMutable(
            initialValue: false,
            StateCategories.Get(typeof(MauiBackgroundState), nameof(IsBackground)));

    // ReSharper disable once InconsistentlySynchronizedField
    public static IState<bool> IsBackground => IsBackgroundMutable;

    // Handlers run synchronously inside Set(true) so platform callbacks can wait.
    public static void RegisterSuspendHandler(Action handler)
    {
        lock (Lock)
            SuspendHandlers.Add(handler);
    }

    public static void Set(bool isBackground)
    {
        Action[] handlersToRun;
        lock (Lock) {
            IsBackgroundMutable.Value = isBackground;
            handlersToRun = isBackground ? SuspendHandlers.ToArray() : [];
        }
        foreach (var handler in handlersToRun)
            try {
                handler();
            }
            catch {
                // ignored
            }
    }
}
