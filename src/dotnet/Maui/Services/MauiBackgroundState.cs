namespace ActualChat.Maui.Services;

/// <summary>
/// Static background state shared across the Maui layer.
/// Set by platform-specific code (AppDelegate on iOS, lifecycle events on Android/Windows).
/// Consumed by <see cref="KvasarStoreSupport"/> to suspend/resume the Kvasar stores.
/// </summary>
public static class MauiBackgroundState
{
    private static readonly Lock Lock = new();
    private static readonly List<Action<bool>> StateHandlers = [];
    private static readonly MutableState<bool> IsBackgroundMutable
        = StateFactory.Default.NewMutable(
            initialValue: false,
            StateCategories.Get(typeof(MauiBackgroundState), nameof(IsBackground)));

    // ReSharper disable once InconsistentlySynchronizedField
    public static IState<bool> IsBackground => IsBackgroundMutable;

    // Handlers run synchronously inside Set so the platform callback (e.g. iOS DidEnterBackground,
    // still holding its background-task assertion) can wait for suspend work to finish.
    public static void RegisterStateHandler(Action<bool> handler)
    {
        lock (Lock)
            StateHandlers.Add(handler);
    }

    public static void Set(bool isBackground)
    {
        Action<bool>[] handlersToRun;
        lock (Lock) {
            IsBackgroundMutable.Value = isBackground;
            handlersToRun = StateHandlers.ToArray();
        }
        foreach (var handler in handlersToRun)
            try {
                handler(isBackground);
            }
            catch {
                // ignored
            }
    }
}
