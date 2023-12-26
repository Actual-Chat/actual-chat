using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public static class MauiLoadingUI
{
    private static readonly Tracer StaticTracer = Tracer.Default[nameof(LoadingUI)];
    private static readonly TaskCompletionSource _whenFirstWebViewCreatedSource = new();
    private static readonly TaskCompletionSource _whenFirstSplashRemovedSource = new();

    public static Task WhenFirstWebViewCreated => _whenFirstWebViewCreatedSource.Task;
    public static readonly Task WhenFirstSplashRemoved = _whenFirstSplashRemovedSource.Task;

    public static void MarkFirstWebViewCreated()
    {
        if (!_whenFirstWebViewCreatedSource.TrySetResult())
            return;

        StaticTracer.Point();
    }

    public static void MarkFirstSplashRemoved()
    {
        if (!_whenFirstSplashRemovedSource.TrySetResult())
            return;

        StaticTracer.Point();
    }
}
