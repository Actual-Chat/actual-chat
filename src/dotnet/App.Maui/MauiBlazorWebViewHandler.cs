using ActualChat.App.Maui.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui;

public partial class MauiBlazorWebViewHandler : BlazorWebViewHandler
{
    private static readonly ITraceSession _trace = TraceSession.Default;

    public ClientAppSettings AppSettings { get; private set; } = null!;
    public UrlMapper UrlMapper { get; private set; } = null!;
    private ILogger Log { get; set; } = NullLogger.Instance;

    public MauiBlazorWebViewHandler()
    {
        // Intentionally use parameterless constructor.
        // Constructor with parameters causes Exception on Android platform:
        // Microsoft.Maui.Platform.ToPlatformException
        // Message = Microsoft.Maui.Handlers.PageHandler found for ActualChat.App.Maui.MainPage is incompatible

        // ReSharper disable once ArrangeConstructorOrDestructorBody
        _trace.Track("MauiBlazorWebViewHandler.Constructor");
    }

    public override void SetMauiContext(IMauiContext mauiContext)
    {
        _trace.Track("MauiBlazorWebViewHandler.SetMauiContext");
        base.SetMauiContext(mauiContext);
        AppSettings = mauiContext.Services.GetRequiredService<ClientAppSettings>();
        UrlMapper = mauiContext.Services.UrlMapper();
        Log = mauiContext.Services.LogFor<MauiBlazorWebViewHandler>();
    }
}
