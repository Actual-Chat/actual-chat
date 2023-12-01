using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public static class MauiLoadingUI
{
    private static readonly Tracer StaticTracer = Tracer.Default[nameof(LoadingUI)];
    private static readonly TaskCompletionSource _whenFirstWebViewCreatedSource = new();

    public static Task WhenFirstWebViewCreated => _whenFirstWebViewCreatedSource.Task;
    public static readonly Task WhenFirstSplashRemoved = WhenSplashRemoved();

    public static void MarkFirstWebViewCreated()
    {
        if (!_whenFirstWebViewCreatedSource.TrySetResult())
            return;

        StaticTracer.Point();
    }

    public static Task WhenSplashRemoved()
        => DispatchToBlazor(async scopedServices => {
            var loadingUI = scopedServices.GetRequiredService<LoadingUI>();
            await Task.WhenAny(
                loadingUI.WhenChatListLoaded.WithDelay(TimeSpan.FromSeconds(0.1)),
                Task.Delay(TimeSpan.FromSeconds(1))
            ).SilentAwait(false);
        }, whenRendered: true);
}
