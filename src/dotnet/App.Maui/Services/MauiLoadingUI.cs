using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public static class MauiLoadingUI
{
    private static readonly Tracer StaticTracer = Tracer.Default[nameof(LoadingUI)];
    private static readonly AsyncTaskMethodBuilder WhenFirstWebViewCreatedSource = AsyncTaskMethodBuilderExt.New();
    private static readonly AsyncTaskMethodBuilder WhenFirstSplashRemovedSource = AsyncTaskMethodBuilderExt.New();

    public static Task WhenFirstWebViewCreated => WhenFirstWebViewCreatedSource.Task;
    public static Task WhenFirstSplashRemoved => WhenFirstSplashRemovedSource.Task;

    public static void MarkFirstWebViewCreated()
    {
        if (!WhenFirstWebViewCreatedSource.TrySetResult())
            return;

        StaticTracer.MethodPoint();
    }

    public static void MarkFirstSplashRemoved()
    {
        if (!WhenFirstSplashRemovedSource.TrySetResult())
            return;

        StaticTracer.MethodPoint();
    }
}
