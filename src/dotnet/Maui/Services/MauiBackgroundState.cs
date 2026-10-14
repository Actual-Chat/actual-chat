namespace ActualChat.Maui.Services;

/// <summary>
/// Static host state shared across the Maui layer: <see cref="IsBackground"/> is set by the OS
/// lifecycle (AppDelegate on iOS, lifecycle events on Android) or, on desktop, by the window
/// observers, which also set <see cref="IsHidden"/>; the phone tracker reads one state for both.
/// <see cref="KvasarStoreSupport"/> suspends/resumes the Kvasar stores on <see cref="IsBackground"/>.
/// </summary>
public static class MauiBackgroundState
{
    private static readonly Lock Lock = new();
    private static readonly List<Action<bool>> StateHandlers = [];
    private static readonly MutableState<bool> IsBackgroundMutable
        = StateFactory.Default.NewMutable(
            initialValue: false,
            StateCategories.Get(typeof(MauiBackgroundState), nameof(IsBackground)));
    private static readonly MutableState<bool> IsHiddenMutable
        = StateFactory.Default.NewMutable(
            initialValue: false,
            StateCategories.Get(typeof(MauiBackgroundState), nameof(IsHidden)));

    // ReSharper disable once InconsistentlySynchronizedField
    public static IState<bool> IsBackground => IsBackgroundMutable;
    public static IState<bool> IsHidden => IsHiddenMutable;

    // Handlers run synchronously inside SetIsBackground so the platform callback (e.g. iOS
    // DidEnterBackground, still holding its background-task assertion) can wait for suspend work to finish.
    public static void RegisterStateHandler(Action<bool> handler)
    {
        lock (Lock)
            StateHandlers.Add(handler);
    }

    public static void SetIsBackground(bool isBackground)
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

    public static void SetIsHidden(bool isHidden)
        => IsHiddenMutable.Value = isHidden;
}
