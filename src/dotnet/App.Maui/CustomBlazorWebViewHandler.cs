using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public class CustomBlazorWebViewHandler : BlazorWebViewHandler
{
    public override void SetMauiContext(IMauiContext mauiContext)
    {
        BlazorWebViewApp.EnsureStarted();

        // Await blazor app is ready
        if (!BlazorWebViewApp.WhenAppReady.IsCompleted) {
            var sw = Stopwatch.GetTimestamp();
            while (!BlazorWebViewApp.WhenAppReady.IsCompleted)
                Thread.Sleep(5);
            var elapsed = Stopwatch.GetElapsedTime(sw);

            var log = StaticLog.Factory.CreateLogger<CustomBlazorWebViewHandler>();
            log.LogDebug("Awaiting BlazorWebViewApp readiness took {Elapsed}ms", (int)elapsed.TotalMilliseconds);
        }

        var services = BlazorWebViewApp.Current.Services;
#if ANDROID
        var newMauiContext = new MauiContext(services, mauiContext.Context!);
#else
        var newMauiContext = new MauiContext(services);
#endif
        base.SetMauiContext(newMauiContext);
    }
}

public record ParentContainerAccessor(IServiceProvider Services);
