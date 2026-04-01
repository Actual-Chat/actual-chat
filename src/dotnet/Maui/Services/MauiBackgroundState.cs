namespace ActualChat.Maui.Services;

/// <summary>
/// Static background state shared across the Maui layer.
/// Set by platform-specific code (AppDelegate on iOS, lifecycle events on Android/Windows).
/// Consumed by <see cref="SQLiteBatchingKvasBackend"/> to auto-suspend when backgrounded.
/// </summary>
public static class MauiBackgroundState
{
    private static readonly Lock Lock = new();
    private static readonly MutableState<bool> IsBackgroundMutable
        = StateFactory.Default.NewMutable(
            initialValue: false,
            StateCategories.Get(typeof(MauiBackgroundState), nameof(IsBackground)));

    // ReSharper disable once InconsistentlySynchronizedField
    public static IState<bool> IsBackground => IsBackgroundMutable;

    public static void Set(bool isBackground)
    {
        lock (Lock)
            IsBackgroundMutable.Value = isBackground;
    }
}
