namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Keeps splash screen / splash UI open in WASM & MAUI.
/// </summary>
public sealed class LoadingUI
{
    private readonly TaskCompletionSource<Unit> _whenLoadedSource = TaskCompletionSourceExt.New<Unit>();
    private readonly TaskCompletionSource<Unit> _whenChatListLoadedSource = TaskCompletionSourceExt.New<Unit>();

    private ILogger Log { get; }
    private Tracer Tracer { get; }

    public Task WhenLoaded => _whenLoadedSource.Task;
    public Task WhenChatListLoaded => _whenChatListLoadedSource.Task;

    public static TimeSpan MauiAppBuildTime { get; private set; }
    public TimeSpan AppCreatedTime { get; private set; }
    public TimeSpan LoadingTime { get; private set; }
    public TimeSpan ChatListLoadTime { get; private set; }

    public LoadingUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Tracer = services.Tracer(GetType());
    }

    public static void MarkMauiAppBuilt(TimeSpan mauiAppBuildTime)
    {
        if (MauiAppBuildTime == default)
            MauiAppBuildTime = mauiAppBuildTime;
    }

    public void MarkAppCreated()
    {
        if (AppCreatedTime == default)
            AppCreatedTime = Tracer.Elapsed;
    }

    public void MarkChatListLoaded()
    {
        if (!_whenChatListLoadedSource.TrySetResult(default))
            return;

        ChatListLoadTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkChatListLoaded));
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.LogInformation($"{nameof(MarkChatListLoaded)}: {ChatListLoadTime.ToShortString()}");
    }

    public void MarkLoaded()
    {
        if (!_whenLoadedSource.TrySetResult(default))
            return;

        LoadingTime = Tracer.Elapsed;
        Tracer.Point(nameof(MarkLoaded));
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.LogInformation($"{nameof(MarkLoaded)}: {LoadingTime.ToShortString()}");
    }
}
