using ActualChat.App.Maui.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler : BlazorWebViewHandler
{
    private static readonly Tracer _trace = Tracer.Default["MauiBlazorWebViewHandler"];

    public ClientAppSettings AppSettings { get; set; } = null!;
    private ILogger Log { get; set; } = NullLogger.Instance;

    public MauiBlazorWebViewHandler()
    {
        // Intentionally use parameterless constructor.
        // Constructor with parameters causes Exception on Android platform:
        // Microsoft.Maui.Platform.ToPlatformException
        // Message = Microsoft.Maui.Handlers.PageHandler found for ActualChat.App.Maui.MainPage is incompatible

        // ReSharper disable once ArrangeConstructorOrDestructorBody
        _trace.Point(".ctor");
    }

    public override void SetMauiContext(IMauiContext mauiContext)
    {
        _trace.Point("SetMauiContext");
        base.SetMauiContext(mauiContext);
        AppSettings = mauiContext.Services.GetRequiredService<ClientAppSettings>();
        Log = mauiContext.Services.LogFor<MauiBlazorWebViewHandler>();
    }
}
