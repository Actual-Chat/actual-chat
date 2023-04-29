using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public sealed partial class MauiBlazorWebViewHandler : BlazorWebViewHandler
{
    private static readonly Tracer Tracer = Tracer.Default[nameof(MauiBlazorWebViewHandler)];
    private ILogger? _log;

    private ILogger Log => _log ??= Services!.LogFor(GetType());

    public MauiBlazorWebViewHandler()
    {
        // Intentionally use parameterless constructor.
        // Constructor with parameters causes Exception on Android platform:
        // Microsoft.Maui.Platform.ToPlatformException
        // Message = Microsoft.Maui.Handlers.PageHandler found for ActualChat.App.Maui.MainPage is incompatible

        // ReSharper disable once ArrangeConstructorOrDestructorBody
        Tracer.Point(".ctor");
    }

    public override void SetMauiContext(IMauiContext mauiContext)
    {
        Tracer.Point(nameof(SetMauiContext));
        base.SetMauiContext(mauiContext);
    }
}
