namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Keeps splash screen / splash UI open in WASM & MAUI.
/// </summary>
public sealed class LoadingUI
{
    private readonly TaskCompletionSource<Unit> _whenDisplayedSource = TaskCompletionSourceExt.New<Unit>();
    private readonly TaskCompletionSource<Unit> _whenLoadedSource = TaskCompletionSourceExt.New<Unit>();
    private readonly TaskCompletionSource<Unit> _whenChatListLoadedSource = TaskCompletionSourceExt.New<Unit>();

    private Tracer Tracer { get; }

    public Task WhenDisplayed => _whenDisplayedSource.Task;
    public Task WhenLoaded => _whenLoadedSource.Task;
    public Task WhenChatListLoaded => _whenChatListLoadedSource.Task;

    public static TimeSpan MauiAppBuildTime { get; private set; }
    public TimeSpan AppCreationTime { get; private set; }
    public TimeSpan DisplayTime { get; private set; }
    public TimeSpan LoadTime { get; private set; }
    public TimeSpan ChatListLoadTime { get; private set; }

    public LoadingUI(IServiceProvider services)
        => Tracer = services.Tracer(GetType());

    public static void MarkMauiAppBuilt(TimeSpan mauiAppBuildTime)
    {
        if (MauiAppBuildTime == default)
            MauiAppBuildTime = mauiAppBuildTime;
    }

    public void MarkAppCreated()
    {
        if (AppCreationTime == default)
            AppCreationTime = Tracer.Elapsed;
    }

    public void MarkDisplayed()
    {
        if (!_whenDisplayedSource.TrySetResult(default))
            return;

        DisplayTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkDisplayed));
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult(default))
            return;

        LoadTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkLoaded));
    }

    public void MarkChatListLoaded()
    {
        if (!_whenChatListLoadedSource.TrySetResult(default))
            return;

        ChatListLoadTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkChatListLoaded));
    }
}
