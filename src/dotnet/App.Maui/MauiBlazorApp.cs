using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui;

public class MauiBlazorApp : AppBase
{
    protected override async Task OnInitializedAsync()
    {
        var session = await AppSettings.SessionTask.ConfigureAwait(true);
        var baseUri = AppSettings.BaseUri;
        MainPage.Current!.SetupSessionCookie(baseUri, session);
        await InitPage(baseUri, session).ConfigureAwait(true);
        SessionProvider.Session = session;
        ScopedServices = Services;
        await base.OnInitializedAsync().ConfigureAwait(true);
    }

    private async Task InitPage(Uri baseUri, Session session)
    {
        using var _ = Tracer.Region("window.App.initPage JS call");
        var jsRuntime = Services.GetRequiredService<IJSRuntime>();
        var script = $"window.App.initPage('{baseUri.ToString()}', '{session.Hash}')";
        await jsRuntime.EvalVoid(script).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // On refreshing page, MAUI dispose PageContext.
        // Which dispose Renderer with all components.
        // And after that container is disposed.
        // So we forget previous scoped services container in advance.
        DiscardScopedServices();
    }
}
